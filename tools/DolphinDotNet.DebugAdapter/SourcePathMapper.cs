using System.Text.Json;

namespace DolphinDotNet.DebugAdapter;

public sealed class SourcePathMapper
{
    private readonly (string Build, string Local)[] _mappings;
    public SourcePathMapper(JsonElement arguments)
    {
        _mappings = arguments.TryGetProperty("sourceFileMap", out var map)
            ? map.EnumerateObject().Select(p => (Clean(p.Name), Clean(p.Value.GetString()!))).ToArray()
            : Array.Empty<(string, string)>();
        if (_mappings.Any(p => p.Build.Length == 0 || p.Local.Length == 0))
            throw new ArgumentException("Source mapping prefixes must not be empty.");
    }
    public string ToBuild(string path) => Map(path, _mappings.Select(p => (p.Local, p.Build)));
    public string ToLocal(string path) => Map(path, _mappings.Select(p => (p.Build, p.Local)));
    private static string Clean(string path) => path.Replace('\\', '/').TrimEnd('/');
    private static string Map(string path, IEnumerable<(string From, string To)> mappings)
    {
        path = Clean(path);
        var matches = mappings.Where(p => path == p.From || path.StartsWith(p.From + "/", StringComparison.Ordinal))
            .OrderByDescending(p => p.From.Length).ToArray();
        if (matches.Length > 1 && matches[0].From.Length == matches[1].From.Length)
            throw new ArgumentException("Ambiguous source path mapping.");
        return matches.Length == 0 ? path : matches[0].To + path[matches[0].From.Length..];
    }
}
