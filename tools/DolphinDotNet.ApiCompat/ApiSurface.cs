using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace DolphinDotNet.ApiCompat;

/// <summary>Inventories externally visible declarations using assembly-independent type names.</summary>
public static class ApiSurface
{
    public static HashSet<string> Read(string path)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (Directory.Exists(path))
        {
            var files = Directory.EnumerateFiles(path, "*.dll", SearchOption.AllDirectories).ToArray();
            if (files.Length == 0) throw new IOException($"No contract assemblies found in '{path}'.");
            foreach (var file in files) result.UnionWith(Read(file));
            return result;
        }

        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var signatures = new SignatureNames();
        foreach (var handle in metadata.TypeDefinitions)
        {
            if (!IsVisible(metadata, handle)) continue;
            var type = metadata.GetTypeDefinition(handle);
            var name = SignatureNames.DefinitionName(metadata, handle);
            result.Add("T:" + name);
            foreach (var methodHandle in type.GetMethods())
            {
                var method = metadata.GetMethodDefinition(methodHandle);
                if (!IsVisible(method.Attributes)) continue;
                result.Add($"M:{name}::{metadata.GetString(method.Name)}:{SignatureNames.Method(method.DecodeSignature(signatures, (object?)null))}");
            }
            foreach (var fieldHandle in type.GetFields())
            {
                var field = metadata.GetFieldDefinition(fieldHandle);
                if (!IsVisible(field.Attributes)) continue;
                var storage = (field.Attributes & FieldAttributes.Static) != 0 ? "static" : "instance";
                result.Add($"F:{name}::{metadata.GetString(field.Name)}:{storage} {field.DecodeSignature(signatures, (object?)null)}");
            }
            foreach (var propertyHandle in type.GetProperties())
            {
                var property = metadata.GetPropertyDefinition(propertyHandle);
                var accessors = property.GetAccessors();
                if (!IsVisible(metadata, accessors.Getter) && !IsVisible(metadata, accessors.Setter)) continue;
                result.Add($"P:{name}::{metadata.GetString(property.Name)}:{SignatureNames.Method(property.DecodeSignature(signatures, (object?)null))}");
            }
            foreach (var eventHandle in type.GetEvents())
            {
                var @event = metadata.GetEventDefinition(eventHandle);
                var accessors = @event.GetAccessors();
                if (!IsVisible(metadata, accessors.Adder) && !IsVisible(metadata, accessors.Remover)) continue;
                result.Add($"E:{name}::{metadata.GetString(@event.Name)}:{signatures.TypeName(metadata, @event.Type)}");
            }
        }
        return result;
    }

    private static bool IsVisible(MetadataReader metadata, TypeDefinitionHandle handle)
    {
        var type = metadata.GetTypeDefinition(handle);
        var visibility = type.Attributes & TypeAttributes.VisibilityMask;
        if (visibility == TypeAttributes.Public) return true;
        return visibility is TypeAttributes.NestedPublic or TypeAttributes.NestedFamily or TypeAttributes.NestedFamORAssem
            && IsVisible(metadata, type.GetDeclaringType());
    }

    private static bool IsVisible(MetadataReader metadata, MethodDefinitionHandle handle) =>
        !handle.IsNil && IsVisible(metadata.GetMethodDefinition(handle).Attributes);

    private static bool IsVisible(MethodAttributes attributes) =>
        (attributes & MethodAttributes.MemberAccessMask) is MethodAttributes.Public or MethodAttributes.Family or MethodAttributes.FamORAssem;

    private static bool IsVisible(FieldAttributes attributes) =>
        (attributes & FieldAttributes.FieldAccessMask) is FieldAttributes.Public or FieldAttributes.Family or FieldAttributes.FamORAssem;
}