using System.Net;
using System.Net.Sockets;
using System.Text;
using DolphinDotNet.Debugger;

Check(RspStop.Parse("T0540:8000510c;thread:1;").ProgramCounter == 0x8000510c, "T stop PC");
Check(RspStop.Parse("S05").ProgramCounter is null, "S stop without PC");
Check(RspStop.Parse("W00").Exited, "exit event");
Expect<InvalidDataException>(() => RspStop.Parse("T05bad"), "bad stop register ignored?"); // Invalid stop field is validated below.
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
    using var listener = new TcpListener(IPAddress.Loopback, 0);
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
    using var listener = new TcpListener(IPAddress.Loopback, 0);
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
    var checksum = (await ReadByteAsync(stream)).ToString("x2");
    var lo = await ReadByteAsync(stream);
    var encoded = Encoding.ASCII.GetString(new byte[] { (byte)Convert.ToInt32(checksum, 16), (byte)lo });
    // Reconstruct exactly the two checksum hex characters.
    Check(Convert.ToInt32(encoded, 16) == (data.Sum(x => (int)x) & 255), "client frame checksum");
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
