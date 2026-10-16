using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;

namespace DolphinDotNet.Debugger;

public sealed record DebugVariable(string Name, string Type, string Group, int StorageBytes,
    IReadOnlySet<uint> VisibleAddresses, string LocationKind, int Offset, int Register, string? UnavailableReason);
public sealed record PrimitiveValue(string Name, string Type, string Value, bool Available);

/// <summary>Read-only scalar inspection from verified DWARF locations. Never evaluates expressions.</summary>
public sealed class PrimitiveVariableReader(RspClient client)
{
    public async Task<PrimitiveValue> ReadAsync(DebugVariable variable, NativeStackFrame frame, bool topFrame,
        CancellationToken token = default)
    {
        byte[] bytes;
        if (variable.LocationKind == "cfaOffset" && frame.CanonicalFrameAddress is uint cfa)
        {
            long pointer = (long)cfa + variable.Offset;
            if (pointer < 0x80000000L || pointer + variable.StorageBytes > 0x81800000L)
                return Unavailable(variable, "DWARF address is outside GameCube memory");
            bytes = await client.ReadMemoryAsync((uint)pointer, variable.StorageBytes, token).ConfigureAwait(false);
        }
        else if (variable.LocationKind == "register" && topFrame && variable.StorageBytes == 4)
            bytes = await client.ReadRegisterAsync(variable.Register, token).ConfigureAwait(false);
        else return Unavailable(variable, variable.UnavailableReason ?? "Frame location is unavailable");
        if (bytes.Length != variable.StorageBytes) throw new InvalidDataException("Truncated primitive variable value.");
        ulong bits = bytes.Length == 8 ? BinaryPrimitives.ReadUInt64BigEndian(bytes) : BinaryPrimitives.ReadUInt32BigEndian(bytes);
        string? value = variable.Type switch
        {
            "System.Boolean" => (bits != 0).ToString().ToLowerInvariant(),
            "System.Byte" => ((byte)bits).ToString(CultureInfo.InvariantCulture),
            "System.SByte" => unchecked((sbyte)bits).ToString(CultureInfo.InvariantCulture),
            "System.Char" => JsonSerializer.Serialize(((char)bits).ToString()),
            "System.Int16" => unchecked((short)bits).ToString(CultureInfo.InvariantCulture),
            "System.UInt16" => ((ushort)bits).ToString(CultureInfo.InvariantCulture),
            "System.Int32" or "System.IntPtr" => unchecked((int)bits).ToString(CultureInfo.InvariantCulture),
            "System.UInt32" or "System.UIntPtr" => ((uint)bits).ToString(CultureInfo.InvariantCulture),
            "System.Int64" when bytes.Length == 8 => unchecked((long)bits).ToString(CultureInfo.InvariantCulture),
            "System.UInt64" when bytes.Length == 8 => bits.ToString(CultureInfo.InvariantCulture),
            "System.Single" => (bytes.Length == 8 ? (float)BitConverter.Int64BitsToDouble((long)bits)
                : BitConverter.Int32BitsToSingle((int)bits)).ToString("R", CultureInfo.InvariantCulture),
            "System.Double" when bytes.Length == 8 => BitConverter.Int64BitsToDouble((long)bits).ToString("R", CultureInfo.InvariantCulture),
            _ => null
        };
        return value is null ? Unavailable(variable, "Unsupported primitive storage") : new(variable.Name, variable.Type, value, true);
    }
    private static PrimitiveValue Unavailable(DebugVariable variable, string reason)
        => new(variable.Name, variable.Type, "<unavailable: " + reason + ">", false);
}
