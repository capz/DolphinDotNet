using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace DolphinDotNet.ApiCompat;

internal sealed class SignatureNames : ISignatureTypeProvider<string, object?>
{
    public static string Method(MethodSignature<string> signature)
    {
        var storage = signature.Header.IsInstance ? "instance" : "static";
        var parameters = string.Join(",", signature.ParameterTypes);
        return $"{storage} {signature.Header.CallingConvention} generic<{signature.GenericParameterCount}> required<{signature.RequiredParameterCount}>({parameters})->{signature.ReturnType}";
    }

    public static string DefinitionName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var type = reader.GetTypeDefinition(handle);
        var parent = type.GetDeclaringType();
        return parent.IsNil ? FullName(reader.GetString(type.Namespace), reader.GetString(type.Name))
            : DefinitionName(reader, parent) + "+" + reader.GetString(type.Name);
    }

    public string TypeName(MetadataReader reader, EntityHandle handle) => handle.Kind switch
    {
        HandleKind.TypeDefinition => GetTypeFromDefinition(reader, (TypeDefinitionHandle)handle, 0),
        HandleKind.TypeReference => GetTypeFromReference(reader, (TypeReferenceHandle)handle, 0),
        HandleKind.TypeSpecification => GetTypeFromSpecification(reader, null, (TypeSpecificationHandle)handle, 0),
        _ => throw new BadImageFormatException("Unsupported type handle in API signature.")
    };

    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => DefinitionName(reader, handle);
    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var type = reader.GetTypeReference(handle);
        return type.ResolutionScope.Kind == HandleKind.TypeReference
            ? GetTypeFromReference(reader, (TypeReferenceHandle)type.ResolutionScope, rawTypeKind) + "+" + reader.GetString(type.Name)
            : FullName(reader.GetString(type.Namespace), reader.GetString(type.Name));
    }
    public string GetTypeFromSpecification(MetadataReader reader, object? context, TypeSpecificationHandle handle, byte rawTypeKind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, context);
    public string GetArrayType(string elementType, ArrayShape shape) =>
        $"{elementType}[rank={shape.Rank};sizes={string.Join(',', shape.Sizes)};bounds={string.Join(',', shape.LowerBounds)}]";
    public string GetSZArrayType(string elementType) => elementType + "[]";
    public string GetByReferenceType(string elementType) => elementType + "&";
    public string GetPointerType(string elementType) => elementType + "*";
    public string GetPinnedType(string elementType) => "pinned " + elementType;
    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => $"{genericType}<{string.Join(',', typeArguments)}>";
    public string GetGenericMethodParameter(object? context, int index) => "!!" + index;
    public string GetGenericTypeParameter(object? context, int index) => "!" + index;
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) =>
        $"{(isRequired ? "modreq" : "modopt")}({modifier}) {unmodifiedType}";
    public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr " + Method(signature);
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => "System." + (typeCode switch
    {
        PrimitiveTypeCode.Boolean => "Boolean", PrimitiveTypeCode.Byte => "Byte", PrimitiveTypeCode.SByte => "SByte",
        PrimitiveTypeCode.Char => "Char", PrimitiveTypeCode.Int16 => "Int16", PrimitiveTypeCode.UInt16 => "UInt16",
        PrimitiveTypeCode.Int32 => "Int32", PrimitiveTypeCode.UInt32 => "UInt32", PrimitiveTypeCode.Int64 => "Int64",
        PrimitiveTypeCode.UInt64 => "UInt64", PrimitiveTypeCode.Single => "Single", PrimitiveTypeCode.Double => "Double",
        PrimitiveTypeCode.IntPtr => "IntPtr", PrimitiveTypeCode.UIntPtr => "UIntPtr", PrimitiveTypeCode.Object => "Object",
        PrimitiveTypeCode.String => "String", PrimitiveTypeCode.Void => "Void", PrimitiveTypeCode.TypedReference => "TypedReference",
        _ => throw new BadImageFormatException($"Unsupported primitive type: {typeCode}.")
    });
    private static string FullName(string ns, string name) => string.IsNullOrEmpty(ns) ? name : ns + "." + name;
}