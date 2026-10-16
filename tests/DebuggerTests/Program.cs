using System.Net;
using System.Net.Sockets;
using System.Text;
using DolphinDotNet.Debugger;

Check(RspStop.Parse("T0540:8000510c;thread:1;").ProgramCounter == 0x8000510c, "T stop PC");
Check(RspStop.Parse("S05").ProgramCounter is null, "S stop without PC");
Check(RspStop.Parse("W00").Exited, "exit event");
Expect<InvalidDataException>(() => RspStop.Parse("T0540:xyz;"), "invalid stop program counter");
await TestSourceBreakpointsAsync();
await TestTcpProtocolAsync();
await TestChecksumsAndTimeoutsAsync();
Console.WriteLine("DolphinDotNet GDB/RSP protocol tests passed.");

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
static void Expect<T>(Action run, string message) where T : Exception
{
    try { run(); }
    catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name + ": " + message);
}
static async Task TestTcpProtocolAsync()
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
    var server = Task.Run(async () =>
    {
        using var peer = await listener.AcceptTcpClientAsync();
        using var stream = peer.GetStream();
        foreach (var (command, reply) in new[]
        {
            ("qSupported", "PacketSize=400;swbreak+"),
            ("Z0,8000510c,4", "OK"),
            ("c", "T0540:8000510c;thread:1;"),
            ("m8000510c,4", "12345678"),
            ("p40", "8000510c"),
            ("s", "T0540:80005110;"),
            ("z0,8000510c,4", "OK")
        })
        {
            var got = await ReadFrameAsync(stream);
            Check(got == command, $"Fake target expected {command}, got {got}");
            await stream.WriteAsync(new byte[] { (byte)'+' });
            await SendReplyAsync(stream, reply);
            Check(await ReadByteAsync(stream) == '+', "Client didn't ACK target reply.");
        }
    });
    await using var client = await RspClient.ConnectTcpAsync("127.0.0.1", port, TimeSpan.FromSeconds(4));
    var features = await client.FeaturesAsync();
    Check(features["PacketSize"] == "400" && features["swbreak"] == "+", "qSupported features");
    await client.SetSoftwareBreakpointAsync(0x8000510c);
    var stop = await client.ContinueAsync();
    Check(stop.ProgramCounter == 0x8000510c && !stop.Exited, "Continue stopped at wrong PPC PC");
    Check(Convert.ToHexString(await client.ReadMemoryAsync(0x8000510c, 4)) == "12345678", "memory read");
    Check(Convert.ToHexString(await client.ReadRegisterAsync(0x40)) == "8000510C", "PowerPC PC register");
    Check((await client.StepInstructionAsync()).ProgramCounter == 0x80005110, "instruction single step");
    await client.ClearSoftwareBreakpointAsync(0x8000510c);
    await server.WaitAsync(TimeSpan.FromSeconds(5));
    Expect<ArgumentOutOfRangeException>(() => client.SetSoftwareBreakpointAsync(0x8000510d).GetAwaiter().GetResult(), "unaligned PPC breakpoint");
    Expect<ArgumentOutOfRangeException>(() => client.ReadMemoryAsync(0, 513).GetAwaiter().GetResult(), "libdb read limit");
    Expect<ArgumentException>(() => RspClient.ConnectUsbGecko("", 1), "missing USB serial device");
    Expect<ArgumentOutOfRangeException>(() => RspClient.ConnectUsbGecko("COM4", -1), "invalid serial rate");
}

static async Task TestChecksumsAndTimeoutsAsync()
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
    var server = Task.Run(async () =>
    {
        using var peer = await listener.AcceptTcpClientAsync();
        using var stream = peer.GetStream();
        Check(await ReadFrameAsync(stream) == "?", "stop status request");
        await stream.WriteAsync(new byte[] { (byte)'+' });
        await SendReplyAsync(stream, "S05", corrupt: true);
        Check(await ReadByteAsync(stream) == '-', "client should NACK bad checksum");
        await SendReplyAsync(stream, "S05");
        Check(await ReadByteAsync(stream) == '+', "client should ACK corrected packet");
        Check(await ReadFrameAsync(stream) == "c", "continue request");
        await stream.WriteAsync(new byte[] { (byte)'+' });
        // No stop reply: client must time out/cancel rather than hang forever.
        await Task.Delay(450);
    });
    await using var client = await RspClient.ConnectTcpAsync("127.0.0.1", port, TimeSpan.FromMilliseconds(180));
    Check((await client.QueryStopAsync()).Signal == 5, "checksum retry stop response");
    using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(120));
    try
    {
        await client.ContinueAsync(cancellation.Token);
        throw new Exception("Expected cancellation while target was executing.");
    }
    catch (OperationCanceledException) { }
    await server.WaitAsync(TimeSpan.FromSeconds(3));
}

static async Task<int> ReadByteAsync(Stream stream)
{
    byte[] one = new byte[1];
    if (await stream.ReadAsync(one) != 1) throw new EndOfStreamException();
    return one[0];
}
static async Task<string> ReadFrameAsync(Stream stream)
{
    Check(await ReadByteAsync(stream) == '$', "RSP frame start");
    var data = new List<byte>();
    int value;
    while ((value = await ReadByteAsync(stream)) != '#') data.Add((byte)value);
    var hi = await ReadByteAsync(stream);
    var lo = await ReadByteAsync(stream);
    var receivedChecksum = Convert.ToInt32(new string(new[] { (char)hi, (char)lo }), 16);
    Check(receivedChecksum == (data.Sum(x => (int)x) & 255), "client frame checksum");
    return Encoding.ASCII.GetString(data.ToArray());
}
static async Task SendReplyAsync(Stream stream, string content, bool corrupt = false)
{
    var bytes = Encoding.ASCII.GetBytes(content);
    int sum = bytes.Sum(b => (int)b) & 255;
    if (corrupt) sum = (sum + 1) & 255;
    var frame = Encoding.ASCII.GetBytes("$" + content + "#" + sum.ToString("x2"));
    await stream.WriteAsync(frame);
}

static async Task TestSourceBreakpointsAsync()
{
    var directory = Path.Combine(Path.GetTempPath(), "dnd-symbols-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var listener = new TcpListener(IPAddress.Loopback, 0);
    try
    {
        var elf = Path.Combine(directory, "Game.elf");
        var source = Path.Combine(directory, "source.json");
        var native = Path.Combine(directory, "addresses.json");
        File.WriteAllText(elf, "test ELF identity");
        File.WriteAllText(source, "{\"schemaVersion\":2}");
        string Hash(string path) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
        var map = new
        {
            schemaVersion = 1, elfSha256 = Hash(elf), sourceMapSha256 = Hash(source),
            methods = new[] { new { breakpoints = new[] {
                new { document = "/build/samples/Game/Program.cs", line = 8, addresses = new[] { "0x8000510c", "0x8000510c", "0x80005110" } },
                new { document = "/build/other/Program.cs", line = 9, addresses = new[] { "0x80005200" } }
            } } }
        };
        File.WriteAllText(native, System.Text.Json.JsonSerializer.Serialize(map));
        var symbols = SourceAddressMap.Load(elf, source, native);
        Check(symbols.Resolve(@"Game\Program.cs", 8).SequenceEqual(new uint[] { 0x8000510c, 0x80005110 }), "normalized/deduplicated source lookup");
        Expect<InvalidDataException>(() => symbols.Resolve("Program.cs", 8), "ambiguous document even if only one has requested line");
        Expect<InvalidDataException>(() => symbols.Resolve("NotProgram.cs", 8), "path-component boundary");
        Expect<InvalidDataException>(() => symbols.Resolve("Game/Program.cs", 7), "unmapped line");
        File.AppendAllText(elf, "stale");
        Expect<InvalidDataException>(() => SourceAddressMap.Load(elf, source, native), "ELF build mismatch");
        listener.Start();
        var server = Task.Run(async () =>
        {
            using var peer = await listener.AcceptTcpClientAsync();
            using var stream = peer.GetStream();
            foreach (var (command, reply) in new[] {
                ("Z0,8000510c,4", "OK"), ("Z0,80005110,4", ""),
                ("z0,8000510c,4", "E01"), ("z0,8000510c,4", "OK") })
            {
                Check(await ReadFrameAsync(stream) == command, "source breakpoint lifecycle command");
                await SendReplyAsync(stream, reply);
                Check(await ReadByteAsync(stream) == '+', "source breakpoint ACK");
            }
            Check(await ReadFrameAsync(stream) == "c", "pause test continue");
            await stream.WriteAsync(new byte[] { (byte)'+' });
            Check(await ReadByteAsync(stream) == 3, "asynchronous interrupt byte");
            await SendReplyAsync(stream, "S02");
            Check(await ReadByteAsync(stream) == '+', "pause ACK");
        });
        await using var client = await RspClient.ConnectTcpAsync("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port);
        var breakpoints = new SourceBreakpoints(client, symbols);
        try { await breakpoints.AddAsync("Game/Program.cs", 8); throw new Exception("Expected unsupported breakpoint failure"); }
        catch (IOException) { }
        Check(breakpoints.Installed.SequenceEqual(new uint[] { 0x8000510c }), "successful partial install remains tracked");
        try { await breakpoints.ClearAsync(); throw new Exception("Expected removal failure"); }
        catch (IOException) { }
        Check(breakpoints.Installed.Count == 1, "failed removal remains tracked");
        await breakpoints.ClearAsync();
        Check(breakpoints.Installed.Count == 0, "successful removal cleared ownership");
        var running = client.ContinueAsync();
        await client.InterruptAsync();
        Check((await running).Signal == 2, "pause while continue waits");
        await server.WaitAsync(TimeSpan.FromSeconds(5));
    }
    finally { listener.Stop(); Directory.Delete(directory, true); }
}
