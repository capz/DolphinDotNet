using System;
using System.IO;

// The directory enumerator returns volume-qualified paths. Directory queries
// require relative paths for the selected storage device.
internal static class BrowserPath
{
    internal static string Relative(bool gcLoader,string path)
    {
        string prefix=gcLoader?"gcloader:/":"dvd:/";
        if(!path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Entry does not belong to the selected volume.",nameof(path));
        return path.Substring(prefix.Length);
    }
    internal static bool IsDiscImage(string path)
    {
        string extension=Path.GetExtension(path);
        return extension.Equals(".iso",StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".gcm",StringComparison.OrdinalIgnoreCase);
    }
}
