using System.Buffers.Binary;
using System.Net.Sockets;
using DolphinDotNet.Debugger;

// This is an automated acceptance probe, not an interactive debugger frontend.
if (args.Length < 6) throw new ArgumentException("Expected PORT ELF SOURCE_MAP ADDRESS_MAP FILE:LINE FILE:LINE [...]");
var symbols = SourceAddressMap.Load(args[1], args[2], args[3]);
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(28));
RspClient? connected = null;
while (connected is null)
{
    try { connected = await RspClient.ConnectTcpAsync("127.0.0.1", int.Parse(args[0]), TimeSpan.FromSeconds(24), deadline.Token); }
    catch (SocketException) { await Task.Delay(150, deadline.Token); }
}
await using var client = connected;
var breakpoints = new SourceBreakpoints(client, symbols);
var hits = new HashSet<uint>();
uint before = 0;
foreach (var specification in args.Skip(4))
{
    var colon = specification.LastIndexOf(':');
    if (colon < 1) throw new ArgumentException("Expected FILE:LINE: " + specification);
    var addresses = await breakpoints.AddAsync(specification[..colon], int.Parse(specification[(colon + 1)..]), deadline.Token);
    var pc = await ProgramCounter(await client.ContinueAsync(deadline.Token));
    if (!addresses.Contains(pc) || !hits.Add(pc)) throw new InvalidDataException("Unexpected or repeated source breakpoint PC.");
    before = pc;
    Console.WriteLine($"DND_DOTNET_SOURCE_BREAKPOINT {specification} PC=0x{pc:x8}");
    await breakpoints.ClearAsync(deadline.Token);
}
var stepped = await ProgramCounter(await client.StepInstructionAsync(deadline.Token));
if (stepped == before) throw new InvalidDataException("Single step did not advance execution.");
Console.WriteLine($"DND_DOTNET_INSTRUCTION_STEP PC=0x{stepped:x8}");
var running = client.ContinueAsync(deadline.Token);
await Task.Delay(200, deadline.Token);
await client.InterruptAsync(deadline.Token);
var paused = await ProgramCounter(await running);
Console.WriteLine($"DND_DOTNET_PAUSE PC=0x{paused:x8}");
// Leave the guest stopped; the smoke harness owns emulator shutdown.

async Task<uint> ProgramCounter(RspStop stop)
{
    if (stop.Exited) throw new InvalidDataException("Target exited unexpectedly: " + stop.Raw);
    if (stop.ProgramCounter is uint pc) return pc;
    var bytes = await client.ReadRegisterAsync(0x40, deadline.Token);
    if (bytes.Length != 4) throw new InvalidDataException("Expected a 32-bit PowerPC PC register.");
    return BinaryPrimitives.ReadUInt32BigEndian(bytes);
}
