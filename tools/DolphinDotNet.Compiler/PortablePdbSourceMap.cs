using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace DolphinDotNet.Compiler;

// Debug information never travels to the console. This host-side table only
// describes the IL that the Value IR importer actually preserved.
internal sealed record ManagedSourceLocation(
    int SequencePointIlOffset, string Document, int StartLine, int StartColumn,
    int EndLine, int EndColumn);

internal sealed record ManagedDebugVariable(string NativeName, string Name, string Type, string Group, int StorageBytes, int ScopeStart, int ScopeEnd, string ValueKind = "primitive");

internal sealed record DebugAssemblyIdentity(string Name, string ModuleVersionId, string PdbId);

internal sealed class PortablePdbSourceMap
{
    public IReadOnlyList<DebugAssemblyIdentity> Assemblies { get; }
    public IReadOnlyDictionary<MethodKey, IReadOnlyList<ManagedDebugVariable>> Variables { get; }
    public IReadOnlyDictionary<MethodKey, IReadOnlyDictionary<int, ManagedSourceLocation>> Methods { get; }

    private PortablePdbSourceMap(
        IReadOnlyList<DebugAssemblyIdentity> assemblies,
        IReadOnlyDictionary<MethodKey, IReadOnlyDictionary<int, ManagedSourceLocation>> methods,
        IReadOnlyDictionary<MethodKey, IReadOnlyList<ManagedDebugVariable>> variables)
    {
        Assemblies = assemblies;
        Methods = methods;
        Variables = variables;
    }

    internal static PortablePdbSourceMap Load(AotCompilation compilation)
    {
        var assemblies = new List<DebugAssemblyIdentity>();
        var mapped = new Dictionary<MethodKey, IReadOnlyDictionary<int, ManagedSourceLocation>>();
        var variables = new Dictionary<MethodKey, IReadOnlyList<ManagedDebugVariable>>();

        foreach (var group in compilation.ValueMethods.GroupBy(method => method.Key.AssemblyName)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var assembly = compilation.Model.Assemblies[group.Key];
            // NuGet libraries commonly ship without symbols. They remain in
            // the native graph but have no source/locals mapping. The entry
            // assembly and any supplied PDB must still pass identity checks.
            if (group.Key != compilation.Entry.AssemblyName &&
                !File.Exists(Path.ChangeExtension(assembly.Path, ".pdb")) &&
                !assembly.PE.ReadDebugDirectory().Any(e => e.Type == System.Reflection.PortableExecutable.DebugDirectoryEntryType.EmbeddedPortablePdb))
            {
                assemblies.Add(new DebugAssemblyIdentity(group.Key,
                    assembly.Metadata.GetGuid(assembly.Metadata.GetModuleDefinition().Mvid).ToString("D"), ""));
                foreach (var method in group) {
                    mapped.Add(method.Key, new Dictionary<int, ManagedSourceLocation>());
                    variables.Add(method.Key, Array.Empty<ManagedDebugVariable>());
                }
                continue;
            }
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
                    var model = compilation.Model.Methods[method.Key];
                    var metadata = assembly.Metadata;
                    var methodDefinition = metadata.GetMethodDefinition(definition);
                    var context = new SignatureAbi.Context(model.TypeArguments ?? Array.Empty<GenericRepresentation>(),
                        model.MethodArguments ?? Array.Empty<GenericRepresentation>());
                    var signature = methodDefinition.DecodeSignature(new SignatureAbi(compilation.Model), context);
                    var names = methodDefinition.GetParameters().Select(h => metadata.GetParameter(h))
                        .Where(p => p.SequenceNumber > 0).ToDictionary(p => p.SequenceNumber - 1, p => metadata.GetString(p.Name));
                    var values = new List<ManagedDebugVariable>();
                    if (method.HasThis && compilation.Model.Types.TryGetValue(method.Key.TypeName, out var owner) && !owner.IsValueType)
                        values.Add(new("a0", "this", method.Key.TypeName, "Arguments", 4, 0, int.MaxValue, "reference"));
                    for (var i = 0; i < signature.ParameterTypes.Length; i++)
                    {
                        var type = signature.ParameterTypes[i];
                        if (IsPrimitive(type.Name) || type.Kind == CilStackKind.ObjectReference) values.Add(new($"a{i + (method.HasThis ? 1 : 0)}",
                            names.GetValueOrDefault(i, $"arg{i}"), type.Name ?? "System.Object", "Arguments",
                            type.Kind is CilStackKind.I8 or CilStackKind.Float ? 8 : 4, 0, int.MaxValue,
                            type.Kind == CilStackKind.ObjectReference ? "reference" : "primitive"));
                    }
                    var body = assembly.PE.GetMethodBody(methodDefinition.RelativeVirtualAddress);
                    if (!body.LocalSignature.IsNil)
                    {
                        var locals = metadata.GetStandaloneSignature(body.LocalSignature)
                            .DecodeLocalSignature(new SignatureAbi(compilation.Model), context);
                        foreach (var handle in reader.GetLocalScopes(definition))
                        {
                            var scope = reader.GetLocalScope(handle);
                            foreach (var variableHandle in scope.GetLocalVariables())
                            {
                                var variable = reader.GetLocalVariable(variableHandle);
                                if ((variable.Attributes & LocalVariableAttributes.DebuggerHidden) != 0 || variable.Index >= locals.Length) continue;
                                var type = locals[variable.Index];
                                if (!IsPrimitive(type.Name) && type.Kind != CilStackKind.ObjectReference) continue;
                                var ir = method.Locals.FirstOrDefault(l => l.Index == variable.Index);
                                if (ir is null || ir.StorageSize > 0) continue;
                                values.Add(new($"l{variable.Index}", reader.GetString(variable.Name), type.Name ?? "System.Object", "Locals",
                                    ir.Kind is IrValueKind.I8 or IrValueKind.R8 ? 8 : 4,
                                    scope.StartOffset, checked(scope.StartOffset + scope.Length),
                                    type.Kind == CilStackKind.ObjectReference ? "reference" : "primitive"));
                            }
                        }
                    }
                    variables.Add(method.Key, values);
                }
            }
        }
        return new PortablePdbSourceMap(assemblies, mapped, variables);
    }
    internal static bool IsPrimitive(string? name) => name is "System.Boolean" or "System.Byte" or "System.SByte"
        or "System.Char" or "System.Int16" or "System.UInt16" or "System.Int32" or "System.UInt32"
        or "System.Int64" or "System.UInt64" or "System.IntPtr" or "System.UIntPtr" or "System.Single" or "System.Double";
}
