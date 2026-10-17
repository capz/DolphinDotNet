using System.Reflection.Metadata;

namespace DolphinDotNet.Compiler;

/// <summary>Host-only layouts use the same payload offsets as generated field access.
/// The address resolver must bind each type to its actual linked ELF symbol.</summary>
internal static class DebugInspectionMetadata
{
    private sealed record Field(string Name, string Owner, string Type, int Offset, int Width, string Kind);
    internal static object Build(AotCompilation compilation)
    {
        var model = compilation.Model;
        var decoded = new Dictionary<(string, string), SignatureAbi.Type>();
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var assembly in model.Assemblies.Values)
        foreach (var handle in assembly.Metadata.TypeDefinitions)
        {
            var definition = assembly.Metadata.GetTypeDefinition(handle);
            var name = MetadataLoader.ResolveTypeName(assembly.Metadata, handle)!;
            if (!compilation.Graph.Types.Contains(name) || !model.Types.TryGetValue(name, out var type) ||
                type.IsValueType || type.IsInterface || definition.GetGenericParameters().Count != 0 ||
                !ValueCBackend.TypeExpr(name).StartsWith("&dnd_type_", StringComparison.Ordinal)) continue;
            candidates.Add(name);
            foreach (var fieldHandle in definition.GetFields())
            {
                var field = assembly.Metadata.GetFieldDefinition(fieldHandle);
                var fieldName = assembly.Metadata.GetString(field.Name);
                decoded[(name, fieldName)] = field.DecodeSignature(new SignatureAbi(model),
                    new SignatureAbi.Context(Array.Empty<GenericRepresentation>(), Array.Empty<GenericRepresentation>()));
            }
        }
        var types = new List<object>();
        foreach (var name in candidates.Order(StringComparer.Ordinal))
        {
            var fields = new List<Field>();
            var complete = true;
            for (string? owner = name; owner is not null && owner != "System.Object"; owner = model.Types[owner].BaseType)
            {
                // Generic/native base layouts are not interpreted as ordinary classes.
                if (!candidates.Contains(owner)) { complete = false; break; }
                foreach (var field in model.Fields.Values.Where(f => f.DeclaringType == owner && !f.IsStatic).OrderBy(f => f.Offset))
                {
                    var type = decoded[(owner, field.Name)];
                    var kind = field.IsReference && type.Kind == CilStackKind.ObjectReference ? "reference"
                        : PortablePdbSourceMap.IsPrimitive(type.Name) && field.StorageSize == 0 ? "primitive" : "unavailable";
                    fields.Add(new(field.Name, owner, type.Name ?? "<unsupported>",
                        8 + ValueCBackend.BasePayloadSize(owner, model) + field.Offset, kind == "reference" ? 4 : field.Size, kind));
                }
            }
            if (complete)
                types.Add(new { name, nativeSymbol = ValueCBackend.TypeExpr(name).TrimStart('&'), kind = "object",
                    size = 8 + ValueCBackend.TotalInstanceSize(name, model),
                    fields = fields.Select(f => new { name = fields.Count(other => other.Name == f.Name) > 1 ? f.Owner + "." + f.Name : f.Name,
                        declaringType = f.Owner, type = f.Type, offset = f.Offset, storageBytes = f.Width, valueKind = f.Kind }).ToArray() });
        }
        types.Add(new { name = "System.String", nativeSymbol = "DND_TYPE_STRING", kind = "string", size = 12,
            fields = Array.Empty<object>() });
        return new { version = 1, pointerSize = 4, objectHeaderSize = 8, stringLengthOffset = 8, stringDataOffset = 12, types };
    }
}
