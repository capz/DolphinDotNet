using System.Buffers.Binary;

namespace DolphinDotNet.Debugger;

public enum UnwindStopReason { EndOfStack, MissingRules, UnavailableRegister, InvalidFrame, FrameLimit }
public sealed record NativeStackFrame(uint ProgramCounter, uint StackPointer, uint? CanonicalFrameAddress,
    NativeMethod? Method, IReadOnlyList<SourceLocation> SourceLocations);
public sealed record StackTraceResult(IReadOnlyList<NativeStackFrame> Frames, UnwindStopReason Reason);

/// <summary>Recover PowerPC callers using verified DWARF CFA rules, without guessing stack layouts.</summary>
public sealed class PowerPcUnwinder(RspClient client, SourceAddressMap symbols)
{
    private const uint MemoryStart = 0x80000000, MemoryEnd = 0x81800000;

    public async Task<uint?> ReadCanonicalFrameAddressAsync(uint pc, CancellationToken token = default)
    {
        var row = symbols.FindUnwindRule(pc);
        if (row is null) return null;
        var value = await ReadRegisterAsync(row.CfaRegister, token).ConfigureAwait(false);
        var cfa = Add(value, row.CfaOffset);
        return cfa is uint address && IsStack(address) ? address : null;
    }

    public async Task<StackTraceResult> ReadStackAsync(uint pc, int frameLimit = 64, CancellationToken token = default)
    {
        if (frameLimit is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(frameLimit));
        var frames = new List<NativeStackFrame>();
        var registers = new Dictionary<int, uint>();
        var seen = new HashSet<(uint, uint)>();
        registers[1] = await ReadRegisterAsync(1, token).ConfigureAwait(false);
        for (var index = 0; index < frameLimit; index++)
        {
            // Caller PCs are return addresses. Attribute and unwind their call instruction.
            var lookup = index == 0 ? pc : pc >= 4 ? pc - 4 : 0;
            if (!registers.TryGetValue(1, out var sp) || !IsStack(sp) || !seen.Add((pc, sp)))
            {
                // Dolphin can stop before startup has initialized r1. Preserve
                // the observed top PC so the IDE has a resumable native frame,
                // without inventing a CFA, caller or source position.
                if (index == 0)
                    frames.Add(new(pc, sp, null, symbols.FindMethod(lookup), symbols.FindExact(lookup)));
                return new(frames, UnwindStopReason.InvalidFrame);
            }
            var row = symbols.FindUnwindRule(lookup);
            uint? cfa = null;
            if (row is not null)
            {
                if (!registers.TryGetValue(row.CfaRegister, out var value))
                {
                    if (index != 0)
                    {
                        frames.Add(new(pc, sp, null, symbols.FindMethod(lookup), symbols.FindExact(lookup)));
                        return new(frames, UnwindStopReason.UnavailableRegister);
                    }
                    value = await ReadRegisterAsync(row.CfaRegister, token).ConfigureAwait(false);
                    registers[row.CfaRegister] = value;
                }
                cfa = Add(value, row.CfaOffset);
            }
            frames.Add(new(pc, sp, cfa, symbols.FindMethod(lookup), symbols.FindExact(lookup)));
            if (row is null) return new(frames, UnwindStopReason.MissingRules);
            if (cfa is not uint callerSp || !IsStack(callerSp) || callerSp < sp)
                return new(frames, UnwindStopReason.InvalidFrame);
            if (!row.Registers.TryGetValue(65, out var returnRule))
                return new(frames, UnwindStopReason.UnavailableRegister);
            if (returnRule.Kind == "undefined") return new(frames, UnwindStopReason.EndOfStack);
            var callerRegisters = new Dictionary<int, uint> { [1] = callerSp };
            foreach (var (number, rule) in row.Registers)
            {
                if (number == 1 || rule.Kind == "undefined") continue;
                uint? restored;
                switch (rule.Kind)
                {
                    case "memory":
                        var address = Add(callerSp, rule.Offset);
                        if (address is not uint pointer || !IsStack(pointer) || pointer > MemoryEnd - 4)
                            return new(frames, UnwindStopReason.InvalidFrame);
                        restored = BinaryPrimitives.ReadUInt32BigEndian(await client.ReadMemoryAsync(pointer, 4, token).ConfigureAwait(false));
                        break;
                    case "same":
                    case "register":
                        var source = rule.Kind == "same" ? number : rule.Register;
                        if (!registers.TryGetValue(source, out var saved))
                        {
                            if (index != 0) { restored = null; break; }
                            saved = await ReadRegisterAsync(source, token).ConfigureAwait(false);
                            registers[source] = saved;
                        }
                        restored = saved;
                        break;
                    default:
                        return new(frames, UnwindStopReason.MissingRules);
                }
                if (restored.HasValue) callerRegisters[number] = restored.Value;
            }
            if (!callerRegisters.TryGetValue(65, out var returnAddress))
                return new(frames, UnwindStopReason.UnavailableRegister);
            if (returnAddress == 0) return new(frames, UnwindStopReason.EndOfStack);
            if ((returnAddress & 3) != 0 || returnAddress < MemoryStart || returnAddress >= MemoryEnd)
                return new(frames, UnwindStopReason.InvalidFrame);
            pc = returnAddress;
            registers = callerRegisters;
        }
        return new(frames, UnwindStopReason.FrameLimit);
    }

    private async Task<uint> ReadRegisterAsync(int dwarfRegister, CancellationToken token)
    {
        // DWARF 65 is LR. GDB's standard PowerPC register ordering places LR at 67.
        var rspRegister = dwarfRegister == 65 ? 67 : dwarfRegister;
        if (dwarfRegister is < 0 or > 31 && dwarfRegister != 65)
            throw new InvalidDataException("Unsupported PowerPC DWARF register.");
        var bytes = await client.ReadRegisterAsync(rspRegister, token).ConfigureAwait(false);
        if (bytes.Length != 4) throw new InvalidDataException("Expected a 32-bit PowerPC register.");
        return BinaryPrimitives.ReadUInt32BigEndian(bytes);
    }

    private static bool IsStack(uint pointer) => pointer >= MemoryStart && pointer < MemoryEnd && (pointer & 3) == 0;
    private static uint? Add(uint value, int offset)
    {
        var result = (long)value + offset;
        return result is > 0 and <= uint.MaxValue ? (uint)result : null;
    }
}
