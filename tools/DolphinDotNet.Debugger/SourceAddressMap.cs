using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace DolphinDotNet.Debugger;

/// <summary>Verified source locations from one ELF and its Portable PDB sidecar.</summary>
public sealed class SourceAddressMap
{
    private readonly List<(string Document, int Line, uint Address)> _points = new();

    public static SourceAddressMap Load(string elf, string sourceMap, string addressMap)
    {
        var source = File.ReadAllBytes(sourceMap);
        using var native = JsonDocument.Parse(File.ReadAllBytes(addressMap));
        using var managed = JsonDocument.Parse(source);
        var root = native.RootElement;
        using var binary = File.OpenRead(elf);
        if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
            managed.RootElement.GetProperty("schemaVersion").GetInt32() != 2 ||
            !string.Equals(root.GetProperty("elfSha256").GetString(), Convert.ToHexString(SHA256.HashData(binary)), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(root.GetProperty("sourceMapSha256").GetString(), Convert.ToHexString(SHA256.HashData(source)), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("ELF and C# debug metadata must belong to the same supported build.");
        var result = new SourceAddressMap();
        foreach (var method in root.GetProperty("methods").EnumerateArray())
        foreach (var point in method.GetProperty("breakpoints").EnumerateArray())
        {
            var document = Normalize(point.GetProperty("document").GetString() ?? "");
            var line = point.GetProperty("line").GetInt32();
            if (document.Length == 0 || line < 1) throw new InvalidDataException("Invalid source location.");
            foreach (var item in point.GetProperty("addresses").EnumerateArray())
            {
                var text = item.GetString() ?? "";
                if (!text.StartsWith("0x", StringComparison.Ordinal) ||
                    !uint.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var address) ||
                    address == 0 || (address & 3) != 0)
                    throw new InvalidDataException("Invalid PowerPC source address: " + text);
                result._points.Add((document, line, address));
            }
        }
        return result;
    }

    public IReadOnlyList<uint> Resolve(string document, int line)
    {
        if (string.IsNullOrWhiteSpace(document)) throw new ArgumentException("Source path is required.", nameof(document));
        if (line < 1) throw new ArgumentOutOfRangeException(nameof(line));
        var path = Normalize(document);
        var documents = _points.Select(p => p.Document).Distinct().Where(p => p == path || p.EndsWith("/" + path, StringComparison.Ordinal)).ToArray();
        if (documents.Length != 1) throw new InvalidDataException($"Source path must match one document: {document} ({documents.Length} matches).");
        var addresses = _points.Where(p => p.Document == documents[0] && p.Line == line).Select(p => p.Address).Distinct().Order().ToArray();
        if (addresses.Length == 0) throw new InvalidDataException($"No native address for {document}:{line}.");
        return addresses;
    }

    private static string Normalize(string path) => string.Join('/', path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
}
