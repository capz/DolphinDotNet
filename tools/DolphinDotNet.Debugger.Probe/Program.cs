using System.Net.Sockets;
using DolphinDotNet.Debugger;

// This is an automated acceptance probe, not an interactive debugger frontend.
if (args.Length < 6) throw new ArgumentException("Expected PORT ELF SOURCE_MAP ADDRESS_MAP FILE:LINE FILE:LINE [...]");
var symbols = SourceAddressMap.Load(args[1], args[2], args[3]);
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
RspClient? connected = null;
while (connected is null)
{
    try { connected = await RspClient.ConnectTcpAsync("127.0.0.1", int.Parse(args[0]), TimeSpan.FromSeconds(40), deadline.Token); }
    catch (SocketException) { await Task.Delay(150, deadline.Token); }
}
await using var client = connected;
var specifications = args.Skip(4).Where(argument => argument != "--stack-steps").ToArray();
if (args.Contains("--stack-steps"))
{
    await StackProbe.RunAsync(client, symbols, specifications, deadline.Token);
    return;
}
var breakpoints = new SourceBreakpoints(client, symbols);
var stepper = new SourceStepper(client, symbols);
var hits = new HashSet<uint>();
uint before = 0;
foreach (var specification in specifications)
{
    var colon = specification.LastIndexOf(':');
    if (colon < 1) throw new ArgumentException("Expected FILE:LINE: " + specification);
    var addresses = await breakpoints.AddAsync(specification[..colon], int.Parse(specification[(colon + 1)..]), deadline.Token);
    var pc = (await stepper.ReadProgramCounterAsync(await client.ContinueAsync(deadline.Token), deadline.Token)).ProgramCounter!.Value;
    if (!addresses.Contains(pc) || !hits.Add(pc)) throw new InvalidDataException("Unexpected or repeated source breakpoint PC.");
    before = pc;
    Console.WriteLine($"DND_DOTNET_SOURCE_BREAKPOINT {specification} PC=0x{pc:x8}");
    await breakpoints.ClearAsync(deadline.Token);
}
var instructionStop = await stepper.ReadProgramCounterAsync(await client.StepInstructionAsync(deadline.Token), deadline.Token);
var stepped = instructionStop.ProgramCounter!.Value;
if (stepped == before) throw new InvalidDataException("Single step did not advance execution.");
Console.WriteLine($"DND_DOTNET_INSTRUCTION_STEP PC=0x{stepped:x8}");
var sourceStep = await stepper.StepIntoAsync(instructionStop, 8192, deadline.Token);
if (sourceStep.Reason != SourceStepReason.SourceLocation || sourceStep.Location?.MethodName is null)
    throw new InvalidDataException($"C# Step Into failed: {sourceStep.Reason} after {sourceStep.Instructions} instructions.");
Console.WriteLine($"DND_DOTNET_SOURCE_STEP {sourceStep.Location.MethodName} {sourceStep.Location.Document}:{sourceStep.Location.Line} PC=0x{sourceStep.Stop.ProgramCounter:x8} instructions={sourceStep.Instructions}");
var running = client.ContinueAsync(deadline.Token);
await Task.Delay(200, deadline.Token);
await client.InterruptAsync(deadline.Token);
var paused = (await stepper.ReadProgramCounterAsync(await running, deadline.Token)).ProgramCounter!.Value;
Console.WriteLine($"DND_DOTNET_PAUSE PC=0x{paused:x8}");
// Leave the guest stopped; the smoke harness owns emulator shutdown.
