using System.Buffers.Binary;

namespace DolphinDotNet.Debugger;

public enum SourceStepReason { SourceLocation, Signal, Exited, InstructionLimit, AmbiguousSource, UnwindUnavailable, UserBreakpoint }

public sealed record SourceStepResult(RspStop Stop, SourceLocation? Location,
    SourceStepReason Reason, int Instructions);

/// <summary>
/// Conservative C# line Step Into for stopped PowerPC targets. The caller serializes
/// session operations; no other command may change execution or breakpoints during a step.
/// </summary>
public sealed class SourceStepper(RspClient client, SourceAddressMap symbols, Func<uint, bool>? breakpointHit = null)
{
    private enum StepMode { Into, Over, Out }
    public async Task<RspStop> ReadProgramCounterAsync(RspStop stop, CancellationToken token = default)
    {
        if (stop.Exited || stop.ProgramCounter.HasValue) return stop;
        var bytes = await client.ReadRegisterAsync(0x40, token).ConfigureAwait(false);
        if (bytes.Length != 4) throw new InvalidDataException("Expected a 32-bit big-endian PowerPC PC register.");
        return stop with { ProgramCounter = BinaryPrimitives.ReadUInt32BigEndian(bytes) };
    }

    /// <summary>
    /// Step past the current mapped line to a distinct mapped line or managed method.
    /// Native instructions with no exact C# location are skipped, up to an explicit budget.
    /// Breakpoints must be removed before stepping; breakpoint arbitration belongs to the session.
    /// A limit result leaves the target stopped and permits a further bounded attempt.
    /// </summary>
    public Task<SourceStepResult> StepIntoAsync(RspStop current, int instructionLimit = 4096,
        CancellationToken token = default) => StepAsync(StepMode.Into, current, instructionLimit, token);

    public Task<SourceStepResult> StepOverAsync(RspStop current, int instructionLimit = 4096,
        CancellationToken token = default) => StepAsync(StepMode.Over, current, instructionLimit, token);

    public Task<SourceStepResult> StepOutAsync(RspStop current, int instructionLimit = 4096,
        CancellationToken token = default) => StepAsync(StepMode.Out, current, instructionLimit, token);

    private async Task<SourceStepResult> StepAsync(StepMode mode, RspStop current, int instructionLimit,
        CancellationToken token)
    {
        if (instructionLimit is < 1 or > 100000) throw new ArgumentOutOfRangeException(nameof(instructionLimit));
        current = await ReadProgramCounterAsync(current, token).ConfigureAwait(false);
        if (current.Exited) return new(current, null, SourceStepReason.Exited, 0);
        var start = symbols.FindExact(current.ProgramCounter!.Value);
        if (start.Count > 1) return new(current, null, SourceStepReason.AmbiguousSource, 0);
        var origin = start.SingleOrDefault();
        var unwinder = new PowerPcUnwinder(client, symbols);
        uint? originCfa = null;
        if (mode != StepMode.Into)
        {
            originCfa = await unwinder.ReadCanonicalFrameAddressAsync(current.ProgramCounter.Value, token).ConfigureAwait(false);
            if (!originCfa.HasValue) return new(current, origin, SourceStepReason.UnwindUnavailable, 0);
        }
        for (var count = 1; count <= instructionLimit; count++)
        {
            token.ThrowIfCancellationRequested();
            current = await client.StepInstructionAsync(token).ConfigureAwait(false);
            if (current.Exited) return new(current, null, SourceStepReason.Exited, count);
            current = await ReadProgramCounterAsync(current, token).ConfigureAwait(false);
            // SIGTRAP is the expected single-step completion. Never hide crashes or interrupts.
            if (current.Signal != 5) return new(current, null, SourceStepReason.Signal, count);
            var locations = symbols.FindExact(current.ProgramCounter!.Value);
            if (breakpointHit?.Invoke(current.ProgramCounter.Value) == true)
                return new(current, locations.Count == 1 ? locations[0] : null, SourceStepReason.UserBreakpoint, count);
            if (locations.Count > 1) return new(current, null, SourceStepReason.AmbiguousSource, count);
            if (locations.Count != 1) continue;
            if (mode == StepMode.Into)
            {
                if (locations[0] != origin) return new(current, locations[0], SourceStepReason.SourceLocation, count);
                continue;
            }
            var cfa = await unwinder.ReadCanonicalFrameAddressAsync(current.ProgramCounter.Value, token).ConfigureAwait(false);
            if (!cfa.HasValue) return new(current, locations[0], SourceStepReason.UnwindUnavailable, count);
            if (cfa > originCfa || (mode == StepMode.Over && cfa == originCfa && locations[0] != origin))
                return new(current, locations[0], SourceStepReason.SourceLocation, count);
        }
        return new(current, null, SourceStepReason.InstructionLimit, instructionLimit);
    }
}
