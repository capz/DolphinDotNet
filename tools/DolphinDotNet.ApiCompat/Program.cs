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
    foreach (var api in missing) Console.WriteLine(api);

    var implemented = reference.Count - missing.Length;
    Console.Error.WriteLine($"Reference APIs: {reference.Count}; implemented: {implemented}; missing: {missing.Length}; API coverage: {Coverage(implemented, reference.Count)}%");
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

static string Coverage(int count, int total) => (total == 0 ? 100 : 100.0 * count / total).ToString("F2", System.Globalization.CultureInfo.InvariantCulture);