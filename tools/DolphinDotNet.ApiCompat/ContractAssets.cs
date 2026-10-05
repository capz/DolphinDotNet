using System.Text.Json;

namespace DolphinDotNet.ApiCompat;

/// <summary>Reads only compile assets selected by a single-target NuGet restore.</summary>
public static class ContractAssets
{
    public static HashSet<string> Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        var targets = root.GetProperty("targets").EnumerateObject().Where(x => !x.Name.Contains('/')).ToArray();
        if (targets.Length != 1) throw new InvalidDataException("Contract assets must contain exactly one framework target.");
        var libraries = root.GetProperty("libraries");
        var packageFolders = root.GetProperty("packageFolders").EnumerateObject().Select(x => x.Name).ToArray();
        var assemblies = new HashSet<string>(StringComparer.Ordinal);
        foreach (var package in targets[0].Value.EnumerateObject())
        {
            if (!package.Value.TryGetProperty("compile", out var compile)) continue;
            foreach (var asset in compile.EnumerateObject())
            {
                if (!asset.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
                if (package.Value.GetProperty("type").GetString() != "package")
                    throw new InvalidDataException("Contract compile assets must be NuGet packages.");
                var packagePath = libraries.GetProperty(package.Name).GetProperty("path").GetString()
                    ?? throw new InvalidDataException("Contract package path is missing.");
                var assembly = packageFolders.Select(folder => Path.Combine(folder, packagePath, asset.Name))
                    .FirstOrDefault(File.Exists) ?? throw new FileNotFoundException($"Resolved contract assembly is missing: {package.Name}/{asset.Name}.");
                assemblies.Add(assembly);
            }
        }
        if (assemblies.Count == 0) throw new InvalidDataException("Contract assets contain no compile assemblies.");
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var assembly in assemblies) result.UnionWith(ApiSurface.Read(assembly));
        return result;
    }
}