using System.Buffers.Binary;

namespace DolphinDotNet.Debugger;

public enum SourceStepReason { SourceLocation, Signal, Exited, InstructionLimit, AmbiguousSource, UnwindUnavailable, UserBreakpoint }

// Instructions counts explicit single-step requests. A run-to-return can execute
// additional target instructions; it remains interruptible through normal Pause.
public sealed record SourceStepResult(RspStop Stop, SourceLocation? Location,
    SourceStepReason Reason, int Instructions);

/// <summary>
/// Conservative C# line Step Into for stopped PowerPC targets. The caller serializes
/// session operations; no other command may change execution or breakpoints during a step.
/// </summary>
public sealed class SourceStepper(RspClient client, SourceAddressMap symbols, Func<uint, bool>? breakpointHit = null,
    Func<uint, CancellationToken, Task<RspStop>>? continueTo = null, Func<bool>? pauseRequested = null)
{
    private readonly Dictionary<uint, uint> _instructions = new();
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
        _instructions.Clear();
        current = await ReadProgramCounterAsync(current, token).ConfigureAwait(false);
        if (current.Exited) return new(current, null, SourceStepReason.Exited, 0);
        var start = symbols.FindExact(current.ProgramCounter!.Value);
        if (start.Count > 1) return new(current, null, SourceStepReason.AmbiguousSource, 0);
        var origin = start.SingleOrDefault();
        var unwinder = new PowerPcUnwinder(client, symbols);
        uint? originCfa = null;
        if (mode != StepMode.Into || symbols.FindUnwindRule(current.ProgramCounter.Value) is not null)
        {
            originCfa = await unwinder.ReadCanonicalFrameAddressAsync(current.ProgramCounter.Value, token).ConfigureAwait(false);
            if (mode != StepMode.Into && !originCfa.HasValue) return new(current, origin, SourceStepReason.UnwindUnavailable, 0);
        }
        var enteredSameSourceCallee = false;
        for (var count = 1; count <= instructionLimit; count++)
        {
            token.ThrowIfCancellationRequested();
            var returnAddress = mode == StepMode.Over && continueTo is not null
                ? await CallReturnAddressAsync(current.ProgramCounter!.Value, token).ConfigureAwait(false) : null;
            if (pauseRequested?.Invoke() == true)
            {
                var pausedLocations = symbols.FindExact(current.ProgramCounter!.Value);
                return new(current, pausedLocations.Count == 1 ? pausedLocations[0] : null, SourceStepReason.Signal, count - 1);
            }
            current = await client.StepInstructionAsync(token).ConfigureAwait(false);
            if (current.Exited) return new(current, null, SourceStepReason.Exited, count);
            current = await ReadProgramCounterAsync(current, token).ConfigureAwait(false);
            // SIGTRAP is the expected single-step completion. Never hide crashes or interrupts.
            if (current.Signal != 5) return new(current, null, SourceStepReason.Signal, count);
            var locations = symbols.FindExact(current.ProgramCounter!.Value);
            if (breakpointHit?.Invoke(current.ProgramCounter.Value) == true)
                return new(current, locations.Count == 1 ? locations[0] : null, SourceStepReason.UserBreakpoint, count);
            if (pauseRequested?.Invoke() == true)
                return new(current, locations.Count == 1 ? locations[0] : null, SourceStepReason.Signal, count);
            if (locations.Count > 1) return new(current, null, SourceStepReason.AmbiguousSource, count);
            if (returnAddress is uint returnPc && current.ProgramCounter != returnPc)
            {
                // Observe recursion before skipping the callee. A return can
                // share both the original source line and native address.
                if (locations.Count == 1 && locations[0] == origin)
                {
                    var calleeCfa = await unwinder.ReadCanonicalFrameAddressAsync(current.ProgramCounter.Value, token).ConfigureAwait(false);
                    enteredSameSourceCallee |= calleeCfa < originCfa;
                }
                current = await ReadProgramCounterAsync(await continueTo!(returnPc, token).ConfigureAwait(false), token).ConfigureAwait(false);
                if (current.Exited) return new(current, null, SourceStepReason.Exited, count);
                locations = symbols.FindExact(current.ProgramCounter!.Value);
                if (current.Signal == 5 && breakpointHit?.Invoke(current.ProgramCounter.Value) == true)
                    return new(current, locations.Count == 1 ? locations[0] : null, SourceStepReason.UserBreakpoint, count);
                // Interrupts and traps away from the return address must stay
                // visible, even when the stub reports every stop as SIGTRAP.
                if (pauseRequested?.Invoke() == true || current.Signal != 5 || current.ProgramCounter != returnPc)
                    return new(current, locations.Count == 1 ? locations[0] : null, SourceStepReason.Signal, count);
                if (locations.Count > 1) return new(current, null, SourceStepReason.AmbiguousSource, count);
            }
            if (locations.Count != 1) continue;
            if (mode == StepMode.Into)
            {
                if (locations[0] != origin) return new(current, locations[0], SourceStepReason.SourceLocation, count);
                // Recursive calls can enter the identical source identity. CFA
                // distinguishes the activation; source-line equality cannot.
                if (originCfa.HasValue)
                {
                    var activation = await unwinder.ReadCanonicalFrameAddressAsync(current.ProgramCounter.Value, token).ConfigureAwait(false);
                    if (activation.HasValue && activation != originCfa)
                        return new(current, locations[0], SourceStepReason.SourceLocation, count);
                }
                continue;
            }
            var cfa = await unwinder.ReadCanonicalFrameAddressAsync(current.ProgramCounter.Value, token).ConfigureAwait(false);
            if (!cfa.HasValue) return new(current, locations[0], SourceStepReason.UnwindUnavailable, count);
            enteredSameSourceCallee |= cfa < originCfa && locations[0] == origin;
            // A recursive call can return to the same source identity. Ordinary
            // calls must still advance past their current caller statement.
            if (cfa > originCfa || (mode == StepMode.Over && cfa == originCfa && (locations[0] != origin || enteredSameSourceCallee)))
                return new(current, locations[0], SourceStepReason.SourceLocation, count);
        }
        return new(current, null, SourceStepReason.InstructionLimit, instructionLimit);
    }

    private async Task<uint?> CallReturnAddressAsync(uint pc, CancellationToken token)
    {
        var method = symbols.FindMethod(pc);
        if (method is null || (pc & 3) != 0 || pc < 0x80000000 || method.End > 0x81800000 || (ulong)pc + 4 >= method.End)
            return null;
        if (!_instructions.TryGetValue(pc, out var instruction))
        {
            // Amortize code reads across a straight-line run. Breakpoints are
            // suspended while reading, so no software trap opcode is cached.
            var size = (int)Math.Min(128u, method.End - pc) & ~3;
            var bytes = await client.ReadMemoryAsync(pc, size, token).ConfigureAwait(false);
            for (var offset = 0; offset < size; offset += 4)
                _instructions[pc + (uint)offset] = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
            instruction = _instructions[pc];
        }
        // Unconditional bl/bla and bctrl only. Conditional linked branches,
        // tail calls and unknown opcodes keep bounded instruction stepping.
        var directCall = instruction >> 26 == 18 && (instruction & 1) != 0;
        var indirectCall = (instruction & 0xffff07ff) == 0x4e800421;
        return directCall || indirectCall ? pc + 4 : null;
    }
}
