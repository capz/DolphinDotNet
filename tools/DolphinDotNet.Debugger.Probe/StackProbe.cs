using DolphinDotNet.Debugger;

internal static class StackProbe
{
    public static async Task RunAsync(RspClient client, SourceAddressMap symbols, string[] specifications, CancellationToken token)
    {
        if (specifications.Length != 2) throw new ArgumentException("Stack probe requires INNER_LINE and OUTER_RETURN_LINE.");
        var breakpoints = new SourceBreakpoints(client, symbols);
        var stepper = new SourceStepper(client, symbols);
        static (string File, int Line) Parse(string spec)
        {
            var colon = spec.LastIndexOf(':');
            return (spec[..colon], int.Parse(spec[(colon + 1)..]));
        }
        var first = Parse(specifications[0]);
        var target = await breakpoints.AddAsync(first.File, first.Line, token);
        var stop = await stepper.ReadProgramCounterAsync(await client.ContinueAsync(token), token);
        if (stop.Exited || !target.Contains(stop.ProgramCounter!.Value)) throw new InvalidDataException("Missing Inner breakpoint.");
        await breakpoints.ClearAsync(token);
        var trace = await new PowerPcUnwinder(client, symbols).ReadStackAsync(stop.ProgramCounter.Value, 32, token);
        var managed = trace.Frames.Where(frame => frame.Method is not null).ToArray();
        if (managed.Length < 3 || managed[0].Method?.Name != "Program.Inner" ||
            managed[1].Method?.Name != "Program.Outer" || managed[2].Method?.Name != "Program.Main")
            throw new InvalidDataException($"Expected Inner -> Outer -> Main stack; got {string.Join(", ", managed.Select(frame => frame.Method!.Name))} ({trace.Reason}).");
        Console.WriteLine($"DND_DOTNET_MANAGED_STACK {string.Join(" -> ", managed.Select(frame => frame.Method!.Name))}");
        var returned = await stepper.StepOutAsync(stop, 4096, token);
        if (returned.Reason != SourceStepReason.SourceLocation || returned.Location?.MethodName != "Program.Outer")
            throw new InvalidDataException("Step Out did not return to Outer: " + returned.Reason);
        Console.WriteLine($"DND_DOTNET_STEP_OUT {returned.Location.MethodName}:{returned.Location.Line}");
        // Step Out can already stop at the next statement. Advance by source
        // location rather than assuming a fixed number of Step Over operations.
        var expected = Parse(specifications[1]);
        var returnPoints = symbols.Resolve(expected.File, expected.Line);
        var secondCallPoints = symbols.Resolve(expected.File, expected.Line - 1);
        var current = returned;
        var crossedSecondCall = false;
        for (var count = 0; count < 4; count++)
        {
            if (current.Reason != SourceStepReason.SourceLocation || current.Location?.MethodName != "Program.Outer" ||
                current.Stop.ProgramCounter is not uint pc)
                throw new InvalidDataException($"Step Over left Outer before its return statement: {current.Reason} {current.Location}.");
            if (returnPoints.Contains(pc))
            {
                if (!crossedSecondCall)
                    throw new InvalidDataException("Reached Outer return without proving Step Over of the second Inner call.");
                Console.WriteLine($"DND_DOTNET_STEP_OVER {current.Location.MethodName}:{current.Location.Line}");
                return;
            }
            crossedSecondCall |= secondCallPoints.Contains(pc);
            Console.WriteLine($"DND_DOTNET_STEP_OVER_FROM {current.Location.MethodName}:{current.Location.Line} PC=0x{pc:x8}");
            current = await stepper.StepOverAsync(current.Stop, 4096, token);
        }
        throw new InvalidDataException("Step Over did not reach Outer return within the source-step budget.");
    }
}
