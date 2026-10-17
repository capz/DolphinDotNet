using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace DolphinDotNet.Debugger;

public sealed record DebugObjectField(string Name, string Type, int Offset, int StorageBytes, string ValueKind);
public sealed record DebugObjectLayout(string Name, string Kind, int Size, IReadOnlyList<DebugObjectField> Fields);
public sealed record ObjectView(uint Address, DebugObjectLayout Layout, int Depth);
public sealed record InspectedValue(string Name, string Type, string Value, ObjectView? Children = null);

public static class ObjectLayouts
{
    public static IReadOnlyDictionary<uint, DebugObjectLayout> Load(JsonElement root)
    {
        var result = new Dictionary<uint, DebugObjectLayout>();
        if (!root.TryGetProperty("inspection", out var data)) return result;
        if (data.GetProperty("version").GetInt32() != 1 || data.GetProperty("pointerSize").GetInt32() != 4 ||
            data.GetProperty("objectHeaderSize").GetInt32() != 8 || data.GetProperty("stringLengthOffset").GetInt32() != 8 ||
            data.GetProperty("stringDataOffset").GetInt32() != 12)
            throw new InvalidDataException("Unsupported inspection ABI.");
        foreach (var type in data.GetProperty("types").EnumerateArray())
        {
            var addressText = type.GetProperty("address").GetString();
            if (addressText is null || !addressText.StartsWith("0x", StringComparison.Ordinal) ||
                !uint.TryParse(addressText.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out var address) ||
                address < 0x80000000 || address >= 0x81800000 || (address & 3) != 0)
                throw new InvalidDataException("Invalid inspection type address.");
            var size = type.GetProperty("size").GetInt32();
            var kind = type.GetProperty("kind").GetString()!;
            if (size < 8 || size > 0x1800000 || kind is not ("object" or "string") || kind == "string" && size != 12)
                throw new InvalidDataException("Invalid inspection type layout.");
            var fields = new List<DebugObjectField>();
            foreach (var field in type.GetProperty("fields").EnumerateArray())
            {
                var offset = field.GetProperty("offset").GetInt32();
                var width = field.GetProperty("storageBytes").GetInt32();
                var valueKind = field.GetProperty("valueKind").GetString()!;
                if (offset < 8 || width < 1 || (long)offset + width > size ||
                    valueKind is not ("primitive" or "reference" or "unavailable") ||
                    valueKind == "reference" && width != 4 || valueKind == "primitive" && width is not (1 or 2 or 4 or 8))
                    throw new InvalidDataException("Invalid inspection field layout.");
                fields.Add(new(field.GetProperty("name").GetString()!, field.GetProperty("type").GetString()!, offset, width, valueKind));
                if (fields.Count > 4096) throw new InvalidDataException("Inspection field count exceeds limit.");
            }
            if (kind == "string" && fields.Count != 0 || !result.TryAdd(address,
                    new(type.GetProperty("name").GetString()!, kind, size, fields)))
                throw new InvalidDataException("Ambiguous inspection type layout.");
        }
        return result;
    }
}

/// <summary>Reads verified class fields and bounded UTF-16 previews without running target code.</summary>
public sealed class ObjectInspector(RspClient client, SourceAddressMap symbols)
{
    public const int MaximumDepth = 4, MaximumStringCharacters = 128, MaximumPageSize = 128;

    public async Task<InspectedValue> ReadAsync(DebugVariable variable, NativeStackFrame frame, bool topFrame,
        CancellationToken token)
    {
        var reader = new PrimitiveVariableReader(client);
        if (variable.ValueKind == "primitive")
        {
            var scalar = await reader.ReadAsync(variable, frame, topFrame, token).ConfigureAwait(false);
            return new(scalar.Name, scalar.Type, scalar.Value);
        }
        var storage = await reader.ReadStorageAsync(variable, frame, topFrame, token).ConfigureAwait(false);
        return storage.Bytes is null ? Unavailable(variable.Name, variable.Type, storage.Error!)
            : await ReadReferenceAsync(variable.Name, variable.Type, BinaryPrimitives.ReadUInt32BigEndian(storage.Bytes), 0, token).ConfigureAwait(false);
    }

    public async Task<InspectedValue> ReadFieldAsync(ObjectView view, DebugObjectField field, CancellationToken token)
    {
        if (field.ValueKind == "unavailable") return Unavailable(field.Name, field.Type, "Unsupported field type");
        try
        {
            var bytes = await client.ReadMemoryAsync(view.Address + (uint)field.Offset, field.StorageBytes, token).ConfigureAwait(false);
            if (field.ValueKind == "reference")
                return await ReadReferenceAsync(field.Name, field.Type, BinaryPrimitives.ReadUInt32BigEndian(bytes), view.Depth + 1, token).ConfigureAwait(false);
            var value = PrimitiveVariableReader.Format(field.Type, bytes);
            return value is null ? Unavailable(field.Name, field.Type, "Unsupported field storage") : new(field.Name, field.Type, value);
        }
        catch (RspRemoteException error) { return Unavailable(field.Name, field.Type, error.Message); }
    }

    private async Task<InspectedValue> ReadReferenceAsync(string name, string declaredType, uint address, int depth, CancellationToken token)
    {
        if (address == 0) return new(name, declaredType, "null");
        if ((address & 3) != 0 || !InMemory(address, 8)) return Unavailable(name, declaredType, "Invalid object reference");
        if (declaredType.EndsWith(']')) return Unavailable(name, declaredType, "Array inspection is not implemented");
        try
        {
            var header = await client.ReadMemoryAsync(address, 4, token).ConfigureAwait(false);
            var layout = symbols.FindObjectLayout(BinaryPrimitives.ReadUInt32BigEndian(header));
            if (layout is null) return Unavailable(name, declaredType, "Runtime type has no verified layout");
            if (!InMemory(address, (uint)layout.Size)) return Unavailable(name, declaredType, "Object extends outside GameCube memory");
            if (declaredType == "System.String" && layout.Kind != "string")
                return Unavailable(name, declaredType, "Runtime type is not a string");
            if (layout.Kind == "string")
            {
                var length = BinaryPrimitives.ReadUInt32BigEndian(await client.ReadMemoryAsync(address + 8, 4, token).ConfigureAwait(false));
                if (!InMemory(address, 12UL + (ulong)length * 2)) return Unavailable(name, layout.Name, "Invalid string length");
                var count = (int)Math.Min(length, MaximumStringCharacters);
                var bytes = count == 0 ? Array.Empty<byte>() : await client.ReadMemoryAsync(address + 12, count * 2, token).ConfigureAwait(false);
                var preview = new StringBuilder("\"");
                for (var i = 0; i < count; i++)
                {
                    var unit = (char)BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(i * 2, 2));
                    preview.Append(unit switch { '\\' => "\\\\", '"' => "\\\"", '\n' => "\\n", '\r' => "\\r", '\t' => "\\t",
                        _ when char.IsControl(unit) || char.IsSurrogate(unit) => "\\u" + ((int)unit).ToString("X4"), _ => unit.ToString() });
                }
                preview.Append('"');
                if (length > count) preview.Append($"… (length={length})");
                return new(name, layout.Name, preview.ToString());
            }
            return new(name, layout.Name, $"{{{layout.Name}}} @ 0x{address:x8}" + (depth >= MaximumDepth ? " <maximum depth>" : ""),
                depth < MaximumDepth && layout.Fields.Count > 0 ? new(address, layout, depth) : null);
        }
        catch (RspRemoteException error) { return Unavailable(name, declaredType, error.Message); }
    }

    private static bool InMemory(uint address, ulong size) => address >= 0x80000000 && (ulong)address + size <= 0x81800000;
    private static InspectedValue Unavailable(string name, string type, string reason) => new(name, type, "<unavailable: " + reason + ">");
}
