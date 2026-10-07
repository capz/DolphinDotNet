using DolphinDotNet.ApiCompat;

var useAssets = args.Length > 0 && args[0] == "--assets";
var inputs = useAssets ? args.Skip(1).ToArray() : args;
if (inputs.Length is < 2 or > 3)
{
    Console.Error.WriteLine("Usage: dnd-api-compat [--assets] <reference.dll-or-directory-or-project.assets.json> <implementation.dll-or-directory> [characterized-api.txt]");
    return 2;
}

try
{
    var reference = useAssets ? ContractAssets.Read(inputs[0]) : ApiSurface.Read(inputs[0]);
    var implementation = ApiSurface.Read(inputs[1]);
    var missing = reference.Except(implementation, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
    var implementedApis = reference.Intersect(implementation, StringComparer.Ordinal).ToArray();
    foreach (var api in missing) Console.WriteLine(api);

    var implemented = reference.Count - missing.Length;
    Console.Error.WriteLine($"Reference APIs: {reference.Count}; implemented: {implemented}; missing: {missing.Length}; API coverage: {Coverage(implemented, reference.Count)}%");
    foreach(var group in reference.GroupBy(Category).OrderBy(g=>g.Key))
    {
        var count=group.Count();var have=group.Count(implementation.Contains);
        Console.Error.WriteLine($"  {group.Key}: {have}/{count} ({Coverage(have,count)}%)");
    }
    var highImpactPrefixes=new[]{"T:System.Object","M:System.Object::","T:System.String","M:System.String::","T:System.Array","M:System.Array::","T:System.Collections","M:System.Collections","T:System.Linq","M:System.Linq"};
    var highImpact=reference.Where(api=>highImpactPrefixes.Any(api.StartsWith)).ToArray();
    var highHave=highImpact.Count(implementation.Contains);
    Console.Error.WriteLine($"High-impact foundation: {highHave}/{highImpact.Length} ({Coverage(highHave,highImpact.Length)}%)");
    if (inputs.Length == 3)
    {
        var characterized = File.ReadLines(inputs[2]).Select(x => x.Trim())
            .Where(x => x.Length > 0 && !x.StartsWith('#')).ToHashSet(StringComparer.Ordinal);
        var validated = reference.Count(api => implementation.Contains(api) && characterized.Contains(api));
        Console.Error.WriteLine($"Runtime-characterized: {validated}; characterized coverage: {Coverage(validated, reference.Count)}%");
    }
    return missing.Length == 0 ? 0 : 1;
}
catch (Exception error) when (error is IOException or UnauthorizedAccessException or BadImageFormatException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
{
    Console.Error.WriteLine($"Could not scan API surface: {error.Message}");
    return 2;
}

static string Category(string api)
{
    var body=api.Length>2?api[2..]:api;var end=body.IndexOf("::",StringComparison.Ordinal);if(end>=0)body=body[..end];
    if(body.StartsWith("System.Collections",StringComparison.Ordinal))return "Collections";
    if(body.StartsWith("System.Linq",StringComparison.Ordinal))return "LINQ";
    if(body.StartsWith("System.Reflection",StringComparison.Ordinal))return "Reflection";
    if(body.StartsWith("System.IO",StringComparison.Ordinal))return "IO";
    if(body.StartsWith("System.Threading",StringComparison.Ordinal))return "Threading";
    return "Core";
}
static string Coverage(int count, int total) => (total == 0 ? 100 : 100.0 * count / total).ToString("F2", System.Globalization.CultureInfo.InvariantCulture);