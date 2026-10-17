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
    private readonly List<NativeMethod> _methods = new();
    private readonly List<UnwindRule> _unwind = new();
    private readonly Dictionary<string, List<DebugVariable>> _variables = new(StringComparer.Ordinal);
    private IReadOnlyDictionary<uint, DebugObjectLayout> _objectLayouts = new Dictionary<uint, DebugObjectLayout>();
    public uint? DebuggerTelemetryAddress { get; private set; }

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
        result._objectLayouts = ObjectLayouts.Load(root);
        if (root.TryGetProperty("debuggerTelemetry", out var telemetry))
        {
            var address = ParseAddress(telemetry.GetProperty("address").GetString());
            if (telemetry.GetProperty("version").GetInt32() != 1 || telemetry.GetProperty("size").GetInt32() != 8 ||
                address < 0x80000000 || address > 0x81800000 - 8)
                throw new InvalidDataException("Unsupported debugger overlay telemetry.");
            result.DebuggerTelemetryAddress = address;
        }
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
            if (method.TryGetProperty("nativeStart", out var nativeStart) && method.TryGetProperty("nativeSize", out var nativeSize))
            {
                var begin = ParseAddress(nativeStart.GetString());
                var size = nativeSize.GetUInt32();
                if (size == 0 || (ulong)begin + size > uint.MaxValue)
                    throw new InvalidDataException("Invalid native method extent.");
                result._methods.Add(new NativeMethod(begin, begin + size, symbol, assembly, methodToken, methodName));
            }
            if (symbol is not null && method.TryGetProperty("variables", out var variables))
            {
                var entries = new List<DebugVariable>();
                foreach (var variable in variables.EnumerateArray())
                {
                    var storage = variable.GetProperty("storageBytes").GetInt32();
                    var group = variable.GetProperty("group").GetString()!;
                    if (storage is not (4 or 8) || group is not ("Arguments" or "Locals"))
                        throw new InvalidDataException("Unsupported debug variable metadata.");
                    var location = variable.GetProperty("location");
                    var kind = location.GetProperty("kind").GetString()!;
                    var valueKind = variable.TryGetProperty("valueKind", out var vk) ? vk.GetString()! : "primitive";
                    if (valueKind is not ("primitive" or "reference") || valueKind == "reference" && storage != 4)
                        throw new InvalidDataException("Unsupported variable value kind.");
                    if (kind is not ("cfaOffset" or "register" or "unavailable")) throw new InvalidDataException("Unsupported variable location.");
                    var register = location.TryGetProperty("register", out var reg) ? reg.GetInt32() : 0;
                    if (register is < 0 or > 31) throw new InvalidDataException("Invalid variable register.");
                    entries.Add(new DebugVariable(variable.GetProperty("name").GetString()!, variable.GetProperty("type").GetString()!,
                        group, storage, variable.GetProperty("visibleAddresses").EnumerateArray().Select(a => ParseAddress(a.GetString())).ToHashSet(),
                        kind, location.TryGetProperty("offset", out var offset) ? offset.GetInt32() : 0, register,
                        location.TryGetProperty("reason", out var reason) ? reason.GetString() : null, valueKind));
                }
                result._variables.Add(symbol, entries);
            }
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
        if (root.TryGetProperty("unwind", out var unwind))
        foreach (var row in unwind.EnumerateArray())
        {
            var begin = ParseAddress(row.GetProperty("start").GetString());
            var end = ParseAddress(row.GetProperty("end").GetString());
            if (end <= begin) throw new InvalidDataException("Invalid unwind extent.");
            var supported = row.GetProperty("supported").GetBoolean();
            var rules = new Dictionary<int, RegisterRule>();
            int cfaRegister = 0, cfaOffset = 0;
            if (supported)
            {
                cfaRegister = row.GetProperty("cfaRegister").GetInt32();
                cfaOffset = row.GetProperty("cfaOffset").GetInt32();
                if (cfaRegister is < 0 or > 31) throw new InvalidDataException("Unsupported CFA register.");
                foreach (var register in row.GetProperty("registers").EnumerateObject())
                {
                    if (!int.TryParse(register.Name, out var number) || (number is < 0 or > 31 && number != 65))
                        throw new InvalidDataException("Unsupported unwind register.");
                    var kind = register.Value.GetProperty("kind").GetString();
                    if (kind is not ("same" or "undefined" or "register" or "memory"))
                        throw new InvalidDataException("Unsupported unwind rule.");
                    rules.Add(number, new RegisterRule(kind,
                        register.Value.TryGetProperty("offset", out var offset) ? offset.GetInt32() : 0,
                        register.Value.TryGetProperty("register", out var sourceRegisterElement) ? sourceRegisterElement.GetInt32() : number));
                }
            }
            result._unwind.Add(new UnwindRule(begin, end, supported, cfaRegister, cfaOffset, rules));
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

    public NativeMethod? FindMethod(uint address)
    {
        var methods = _methods.Where(method => method.Start <= address && address < method.End).ToArray();
        return methods.Length == 1 ? methods[0] : null;
    }

    public IReadOnlyList<DebugVariable> FindVariables(uint address)
    {
        var symbol = FindMethod(address)?.Symbol;
        return symbol is not null && _variables.TryGetValue(symbol, out var variables)
            ? variables.Where(v => v.VisibleAddresses.Contains(address)).ToArray() : Array.Empty<DebugVariable>();
    }

    public UnwindRule? FindUnwindRule(uint address)
    {
        var rows = _unwind.Where(row => row.Start <= address && address < row.End).ToArray();
        return rows.Length == 1 && rows[0].Supported ? rows[0] : null;
    }

    public DebugObjectLayout? FindObjectLayout(uint typeAddress) => _objectLayouts.GetValueOrDefault(typeAddress);

    private static uint ParseAddress(string? text)
    {
        if (text is null || !text.StartsWith("0x", StringComparison.Ordinal) ||
            !uint.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var address) ||
            address == 0 || (address & 3) != 0)
            throw new InvalidDataException("Invalid PowerPC metadata address.");
        return address;
    }

    private static string Normalize(string path) => string.Join('/', path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
}

public sealed record NativeMethod(uint Start, uint End, string? Symbol, string? Assembly, int? MethodToken, string? Name);
public sealed record RegisterRule(string Kind, int Offset, int Register);
public sealed record UnwindRule(uint Start, uint End, bool Supported, int CfaRegister, int CfaOffset,
    IReadOnlyDictionary<int, RegisterRule> Registers);
