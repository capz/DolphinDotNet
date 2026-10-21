using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;

namespace DolphinDotNet.Debugger;

public sealed record DebugVariable(string Name, string Type, string Group, int StorageBytes,
    IReadOnlySet<uint> VisibleAddresses, string LocationKind, int Offset, int Register, string? UnavailableReason,
    string ValueKind = "primitive", IReadOnlyList<DebugAddressRange>? VisibleRanges = null);
/// <summary>A verified half-open native interval; its end is never visible.</summary>
public sealed record DebugAddressRange(uint Start, uint End);
public sealed record PrimitiveValue(string Name, string Type, string Value, bool Available);

/// <summary>Read-only scalar inspection from verified DWARF locations. Never evaluates expressions.</summary>
public sealed class PrimitiveVariableReader(RspClient client)
{
    public async Task<PrimitiveValue> ReadAsync(DebugVariable variable, NativeStackFrame frame, bool topFrame,
        CancellationToken token = default)
    {
        var storage = await ReadStorageAsync(variable, frame, topFrame, token).ConfigureAwait(false);
        if (storage.Bytes is null) return Unavailable(variable, storage.Error!);
        var value = Format(variable.Type, storage.Bytes);
        return value is null ? Unavailable(variable, "Unsupported primitive storage") : new(variable.Name, variable.Type, value, true);
    }

    public async Task<(byte[]? Bytes, string? Error)> ReadStorageAsync(DebugVariable variable, NativeStackFrame frame,
        bool topFrame, CancellationToken token = default)
    {
        byte[] bytes;
        try
        {
            if (variable.LocationKind == "cfaOffset" && frame.CanonicalFrameAddress is uint cfa)
            {
                long pointer = (long)cfa + variable.Offset;
                if (pointer < 0x80000000L || pointer + variable.StorageBytes > 0x81800000L)
                    return (null, "DWARF address is outside GameCube memory");
                bytes = await client.ReadMemoryAsync((uint)pointer, variable.StorageBytes, token).ConfigureAwait(false);
            }
            else if (variable.LocationKind == "register" && topFrame && variable.StorageBytes == 4)
                bytes = await client.ReadRegisterAsync(variable.Register, token).ConfigureAwait(false);
            else return (null, variable.UnavailableReason ?? "Frame location is unavailable");
        }
        catch (RspRemoteException error) { return (null, error.Message); }
        if (bytes.Length != variable.StorageBytes) throw new InvalidDataException("Truncated primitive variable value.");
        return (bytes, null);
    }

    public static string? Format(string type, byte[] bytes)
    {
        ulong bits = bytes.Length switch { 1 => bytes[0], 2 => BinaryPrimitives.ReadUInt16BigEndian(bytes),
            4 => BinaryPrimitives.ReadUInt32BigEndian(bytes), 8 => BinaryPrimitives.ReadUInt64BigEndian(bytes),
            _ => throw new InvalidDataException("Unsupported scalar storage width.") };
        return type switch
        {
            "System.Boolean" => (bits != 0).ToString().ToLowerInvariant(),
            "System.Byte" => ((byte)bits).ToString(CultureInfo.InvariantCulture),
            "System.SByte" => unchecked((sbyte)bits).ToString(CultureInfo.InvariantCulture),
            // JSON replaces isolated UTF-16 surrogates; preserve the actual
            // Char code unit instead of displaying a replacement character.
            "System.Char" => char.IsSurrogate((char)bits) ? "\"\\u" + ((ushort)bits).ToString("X4", CultureInfo.InvariantCulture) + "\""
                : JsonSerializer.Serialize(((char)bits).ToString()),
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
    }
    private static PrimitiveValue Unavailable(DebugVariable variable, string reason)
        => new(variable.Name, variable.Type, "<unavailable: " + reason + ">", false);
}
