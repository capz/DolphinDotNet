using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace DolphinDotNet.Debugger;

/// <summary>An exact native address mapped to a managed method and source line.</summary>
public sealed record SourceLocation(string Document, int Line, string? NativeSymbol,
    string? Assembly, int? MethodToken, string? MethodName);

/// <summary>Verified source locations from one ELF and its Portable PDB sidecar.</summary>
public sealed class SourceAddressMap
{
    private readonly List<(string Document, int Line, uint Address)> _points = new();
    private readonly Dictionary<uint, List<SourceLocation>> _locations = new();

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
        var identities = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (managed.RootElement.TryGetProperty("methods", out var managedMethods))
            foreach (var method in managedMethods.EnumerateArray())
                identities.Add(method.GetProperty("symbol").GetString()!, method);
        foreach (var method in root.GetProperty("methods").EnumerateArray())
        {
            var symbol = method.TryGetProperty("symbol", out var symbolElement) ? symbolElement.GetString() : null;
            var assembly = method.TryGetProperty("assembly", out var assemblyElement) ? assemblyElement.GetString() : null;
            int? methodToken = method.TryGetProperty("methodToken", out var tokenElement) ? tokenElement.GetInt32() : null;
            string? methodName = symbol;
            if (symbol is not null && identities.TryGetValue(symbol, out var identity))
                methodName = identity.GetProperty("declaringType").GetString() + "." + identity.GetProperty("name").GetString();
            foreach (var point in method.GetProperty("breakpoints").EnumerateArray())
            {
                var originalDocument = point.GetProperty("document").GetString() ?? "";
                var document = Normalize(originalDocument);
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
                    if (!result._locations.TryGetValue(address, out var locations))
                        result._locations.Add(address, locations = new());
                    var location = new SourceLocation(originalDocument, line, symbol, assembly, methodToken, methodName);
                    if (!locations.Contains(location)) locations.Add(location);
                }
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

    /// <summary>Exact DWARF addresses only. Never carries a C# line through an unmapped native range.</summary>
    public IReadOnlyList<SourceLocation> FindExact(uint programCounter)
        => _locations.TryGetValue(programCounter, out var locations) ? locations.ToArray() : Array.Empty<SourceLocation>();

    private static string Normalize(string path) => string.Join('/', path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
}
