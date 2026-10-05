using System.Reflection.Metadata;

namespace DolphinDotNet.Compiler;

internal static class MetadataLoader
{
    public static CompilationModel Load(MetadataReader md)
    {
        var model = new CompilationModel { Metadata = md };
        foreach (var th in md.TypeDefinitions)
        {
            var type = md.GetTypeDefinition(th);
            var ns = md.GetString(type.Namespace);
            var name = md.GetString(type.Name);
            if (name == "<Module>") continue;
            var full = string.IsNullOrEmpty(ns) ? name : ns + "." + name;
            int instanceOffset = 0;

            foreach (var fh in type.GetFields())
            {
                var field = md.GetFieldDefinition(fh);
                if ((field.Attributes & System.Reflection.FieldAttributes.Static) != 0) continue;
                var (size, reference) = FieldLayout(md, field.Signature);
                var fieldName = md.GetString(field.Name);
                model.Fields[(full, fieldName)] = new FieldModel(full, fieldName, instanceOffset, reference);
                instanceOffset += Math.Max(4, size);
            }

            model.Types[full] = new TypeModel(ns, name, full, ResolveTypeName(md, type.BaseType), instanceOffset);

            foreach (var mh in type.GetMethods())
            {
                var method = md.GetMethodDefinition(mh);
                var key = new MethodKey(full, md.GetString(method.Name));
                model.Methods[key] = new MethodModel(key, mh,
                    (method.Attributes & System.Reflection.MethodAttributes.Static) != 0,
                    ReadParameterCount(md, method.Signature));
            }
        }
        return model;
    }

    public static string? ResolveTypeName(MetadataReader md, EntityHandle handle)
    {
        if (handle.IsNil) return null;
        if (handle.Kind == HandleKind.TypeDefinition)
        {
            var t = md.GetTypeDefinition((TypeDefinitionHandle)handle);
            return Full(md.GetString(t.Namespace), md.GetString(t.Name));
        }
        if (handle.Kind == HandleKind.TypeReference)
        {
            var t = md.GetTypeReference((TypeReferenceHandle)handle);
            return Full(md.GetString(t.Namespace), md.GetString(t.Name));
        }
        return null;
    }

    private static int ReadParameterCount(MetadataReader md, BlobHandle signature)
    {
        var r = md.GetBlobReader(signature);
        var header = r.ReadSignatureHeader();
        if (header.IsGeneric) r.ReadCompressedInteger();
        return r.ReadCompressedInteger();
    }

    private static (int Size, bool Reference) FieldLayout(MetadataReader md, BlobHandle signature)
    {
        var r = md.GetBlobReader(signature);
        r.ReadSignatureHeader();
        var code = r.ReadSignatureTypeCode();
        return code switch
        {
            SignatureTypeCode.Boolean or SignatureTypeCode.Byte or SignatureTypeCode.SByte => (1, false),
            SignatureTypeCode.Char or SignatureTypeCode.Int16 or SignatureTypeCode.UInt16 => (2, false),
            SignatureTypeCode.Int64 or SignatureTypeCode.UInt64 or SignatureTypeCode.Double => (8, false),
            SignatureTypeCode.String or SignatureTypeCode.Object or SignatureTypeCode.Class or SignatureTypeCode.SZArray or SignatureTypeCode.Array => (4, true),
            _ => (4, false)
        };
    }

    private static string Full(string ns, string name) => string.IsNullOrEmpty(ns) ? name : ns + "." + name;
}
