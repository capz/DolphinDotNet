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
        // Finish the first call statement, then step over the second Inner call.
        var second = await stepper.StepOverAsync(returned.Stop, 4096, token);
        if (second.Reason != SourceStepReason.SourceLocation || second.Location?.MethodName != "Program.Outer")
            throw new InvalidDataException("Step Over did not reach the second caller statement: " + second.Reason);
        var final = await stepper.StepOverAsync(second.Stop, 4096, token);
        var expected = Parse(specifications[1]);
        if (final.Reason != SourceStepReason.SourceLocation || final.Location?.MethodName != "Program.Outer" ||
            !symbols.Resolve(expected.File, expected.Line).Contains(final.Stop.ProgramCounter!.Value))
            throw new InvalidDataException($"Step Over entered a callee or missed Outer return: {final.Reason} {final.Location}.");
        Console.WriteLine($"DND_DOTNET_STEP_OVER {final.Location.MethodName}:{final.Location.Line}");
    }
}
