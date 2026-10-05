using DolphinDotNet.ApiCompat;

if (args.Length is < 2 or > 3)
{
    Console.Error.WriteLine("Usage: dnd-api-compat <reference.dll-or-directory> <implementation.dll-or-directory> [characterized-api.txt]");
    return 2;
}

try
{
    var reference = ApiSurface.Read(args[0]);
    var implementation = ApiSurface.Read(args[1]);
    var missing = reference.Except(implementation, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
    foreach (var api in missing) Console.WriteLine(api);

    var implemented = reference.Count - missing.Length;
    Console.Error.WriteLine($"Reference APIs: {reference.Count}; implemented: {implemented}; missing: {missing.Length}; API coverage: {Coverage(implemented, reference.Count):F2}%");
    if (args.Length == 3)
    {
        var characterized = File.ReadLines(args[2]).Select(x => x.Trim())
            .Where(x => x.Length > 0 && !x.StartsWith('#')).ToHashSet(StringComparer.Ordinal);
        var validated = reference.Count(api => implementation.Contains(api) && characterized.Contains(api));
        Console.Error.WriteLine($"Runtime-characterized: {validated}; characterized coverage: {Coverage(validated, reference.Count):F2}%");
    }
    return missing.Length == 0 ? 0 : 1;
}
catch (Exception error) when (error is IOException or UnauthorizedAccessException or BadImageFormatException)
{
    Console.Error.WriteLine($"Could not scan API surface: {error.Message}");
    return 2;
}

static double Coverage(int count, int total) => total == 0 ? 100 : 100.0 * count / total;