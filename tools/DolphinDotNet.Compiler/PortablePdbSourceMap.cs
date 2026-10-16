using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace DolphinDotNet.Compiler;

// Debug information never travels to the console. This host-side table only
// describes the IL that the Value IR importer actually preserved.
internal sealed record ManagedSourceLocation(
    int SequencePointIlOffset, string Document, int StartLine, int StartColumn,
    int EndLine, int EndColumn);

internal sealed record DebugAssemblyIdentity(string Name, string ModuleVersionId, string PdbId);

internal sealed class PortablePdbSourceMap
{
    public IReadOnlyList<DebugAssemblyIdentity> Assemblies { get; }
    public IReadOnlyDictionary<MethodKey, IReadOnlyDictionary<int, ManagedSourceLocation>> Methods { get; }

    private PortablePdbSourceMap(
        IReadOnlyList<DebugAssemblyIdentity> assemblies,
        IReadOnlyDictionary<MethodKey, IReadOnlyDictionary<int, ManagedSourceLocation>> methods)
    {
        Assemblies = assemblies;
        Methods = methods;
    }

    internal static PortablePdbSourceMap Load(AotCompilation compilation)
    {
        var assemblies = new List<DebugAssemblyIdentity>();
        var mapped = new Dictionary<MethodKey, IReadOnlyDictionary<int, ManagedSourceLocation>>();

        foreach (var group in compilation.ValueMethods.GroupBy(method => method.Key.AssemblyName)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var assembly = compilation.Model.Assemblies[group.Key];
            // This API validates the CodeView ID against the PDB (including
            // embedded PDBs). A stale same-named PDB must never be accepted.
            if (!assembly.PE.TryOpenAssociatedPortablePdb(assembly.Path,
                    path => File.Exists(path) ? File.OpenRead(path) : null,
                    out var provider, out _) || provider is null)
                throw new InvalidDataException(
                    $"No matching Portable PDB found for '{assembly.Path}'. " +
                    "Rebuild the managed project with DebugType=portable (or embedded) and keep the PDB beside its DLL.");

            using (provider)
            {
                var reader = provider.GetMetadataReader();
                var pdbId = reader.DebugMetadataHeader?.Id
                    ?? throw new InvalidDataException($"Invalid Portable PDB metadata for '{assembly.Path}'.");
                assemblies.Add(new DebugAssemblyIdentity(group.Key,
                    assembly.Metadata.GetGuid(assembly.Metadata.GetModuleDefinition().Mvid).ToString("D"),
                    Convert.ToHexString(pdbId.ToArray())));

                foreach (var method in group)
                {
                    var definition = compilation.Model.Methods[method.Key].Handle;
                    var debugInfo = reader.GetMethodDebugInformation(definition);
                    var points = debugInfo.GetSequencePoints().ToArray();
                    var allOffsets = method.Blocks.SelectMany(block => block.Instructions)
                        .Select(instruction => instruction.CilOffset)
                        .Concat(method.Blocks.Select(block => block.Terminator?.CilOffset ?? -1))
                        .Where(offset => offset >= 0).Distinct().OrderBy(offset => offset).ToArray();

                    var locations = new SortedDictionary<int, ManagedSourceLocation>();
                    ManagedSourceLocation? active = null;
                    var index = 0;
                    foreach (var ilOffset in allOffsets)
                    {
                        // Hidden sequence points MUST reset the current location.
                        // Carrying the last visible line through a hidden range
                        // causes breakpoints to stop in compiler-generated code.
                        while (index < points.Length && points[index].Offset <= ilOffset)
                        {
                            var point = points[index++];
                            var document = point.Document.IsNil ? debugInfo.Document : point.Document;
                            active = point.IsHidden || document.IsNil ? null
                                : new ManagedSourceLocation(point.Offset,
                                    reader.GetString(reader.GetDocument(document).Name),
                                    point.StartLine, point.StartColumn, point.EndLine, point.EndColumn);
                        }
                        if (active is not null)
                            locations.Add(ilOffset, active);
                    }
                    mapped.Add(method.Key, locations);
                }
            }
        }
        return new PortablePdbSourceMap(assemblies, mapped);
    }
}
