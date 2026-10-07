using System.Reflection.Metadata;

namespace DolphinDotNet.Compiler;

internal enum GenericRepresentationKind { PointerSized, ValueType }

internal readonly record struct GenericRepresentation(GenericRepresentationKind Kind,int Size)
{
    public bool RequiresSpecialization=>Kind==GenericRepresentationKind.ValueType&&Size>4;
    public string Key=>RequiresSpecialization?$"v{Size}":"p";
}

// GameCube is 32-bit: only representations wider than one machine word need a distinct body.
internal static class GenericSharing
{
    public static IReadOnlyList<GenericRepresentation> ReadMethodArguments(MetadataReader md,MethodSpecificationHandle handle,CompilationModel model)
    {
        var spec=md.GetMethodSpecification(handle);var reader=md.GetBlobReader(spec.Signature);
        var header=reader.ReadSignatureHeader();
        if(header.Kind!=SignatureKind.MethodSpecification)throw new InvalidDataException("Invalid generic method specification.");
        var count=reader.ReadCompressedInteger();var result=new GenericRepresentation[count];
        for(var i=0;i<count;i++)result[i]=ReadRepresentation(md,ref reader,model);
        return result;
    }

    public static GenericRepresentation ReadRepresentation(MetadataReader md,ref BlobReader reader,CompilationModel model)
    {
        var code=reader.ReadSignatureTypeCode();
        return code switch
        {
            SignatureTypeCode.Boolean or SignatureTypeCode.Byte or SignatureTypeCode.SByte=>new(GenericRepresentationKind.PointerSized,1),
            SignatureTypeCode.Char or SignatureTypeCode.Int16 or SignatureTypeCode.UInt16=>new(GenericRepresentationKind.PointerSized,2),
            SignatureTypeCode.Int32 or SignatureTypeCode.UInt32 or SignatureTypeCode.Single or SignatureTypeCode.IntPtr or SignatureTypeCode.UIntPtr=>new(GenericRepresentationKind.PointerSized,4),
            SignatureTypeCode.Int64 or SignatureTypeCode.UInt64 or SignatureTypeCode.Double=>new(GenericRepresentationKind.ValueType,8),
            SignatureTypeCode.String or SignatureTypeCode.Object or SignatureTypeCode.SZArray or SignatureTypeCode.Array=>new(GenericRepresentationKind.PointerSized,4),
            SignatureTypeCode.TypeHandle=>FromTypeHandle(md,reader.ReadTypeHandle(),model),
            SignatureTypeCode.GenericTypeInstance=>ReadGenericInstance(md,ref reader,model),
            _=>new(GenericRepresentationKind.PointerSized,4)
        };
    }

    public static int ReadGenericLocalStorage(MetadataReader md,ref BlobReader reader,CompilationModel model)
    {
        if(reader.ReadSignatureTypeCode()!=SignatureTypeCode.GenericTypeInstance||reader.ReadSignatureTypeCode()!=SignatureTypeCode.TypeHandle)return 0;
        var definition=MetadataLoader.ResolveTypeName(md,reader.ReadTypeHandle());var count=reader.ReadCompressedInteger();
        var args=new GenericRepresentation[count];for(var i=0;i<count;i++)args[i]=ReadRepresentation(md,ref reader,model);
        if(definition=="System.Nullable`1"&&args.Length==1)return 4+((args[0].Size+3)&~3);
        if(definition is not null&&model.Types.TryGetValue(definition,out var type)&&type.IsValueType)return Math.Max(1,type.InstanceSize);
        return 0;
    }

    public static string SpecializationSuffix(IReadOnlyList<GenericRepresentation> typeArgs,IReadOnlyList<GenericRepresentation> methodArgs)
    {
        var all=typeArgs.Concat(methodArgs).ToArray();
        return all.Any(x=>x.RequiresSpecialization)?"|g:"+string.Join(",",all.Select(x=>x.Key)):string.Empty;
    }

    public static IReadOnlyList<GenericRepresentation> ReadTypeArguments(MetadataReader md,TypeSpecificationHandle handle,CompilationModel model)
    {
        var reader=md.GetBlobReader(md.GetTypeSpecification(handle).Signature);
        if(reader.ReadSignatureTypeCode()!=SignatureTypeCode.GenericTypeInstance||reader.ReadSignatureTypeCode()!=SignatureTypeCode.TypeHandle)return Array.Empty<GenericRepresentation>();
        reader.ReadTypeHandle();var count=reader.ReadCompressedInteger();var result=new GenericRepresentation[count];
        for(var i=0;i<count;i++)result[i]=ReadRepresentation(md,ref reader,model);
        return result;
    }

    public static GenericAbi BuildAbi(MetadataReader md,MethodModel definition,IReadOnlyList<GenericRepresentation> typeArgs,IReadOnlyList<GenericRepresentation> methodArgs)
    {
        var method=md.GetMethodDefinition(definition.Handle);var reader=md.GetBlobReader(method.Signature);var header=reader.ReadSignatureHeader();
        if(header.IsGeneric)reader.ReadCompressedInteger();var count=reader.ReadCompressedInteger();
        var ret=ReadAbiKind(md,ref reader,typeArgs,methodArgs);var parameters=new CilStackKind[count];
        for(var i=0;i<count;i++)parameters[i]=ReadAbiKind(md,ref reader,typeArgs,methodArgs)??CilStackKind.Unknown;
        return new GenericAbi(parameters,ret);
    }

    private static CilStackKind? ReadAbiKind(MetadataReader md,ref BlobReader reader,IReadOnlyList<GenericRepresentation> typeArgs,IReadOnlyList<GenericRepresentation> methodArgs)
    {
        var code=reader.ReadSignatureTypeCode();
        if(code==SignatureTypeCode.Void)return null;
        if(code==SignatureTypeCode.GenericMethodParameter){var i=reader.ReadCompressedInteger();return AbiKind(methodArgs[i]);}
        if(code==SignatureTypeCode.GenericTypeParameter){var i=reader.ReadCompressedInteger();return AbiKind(typeArgs[i]);}
        return code switch
        {
            SignatureTypeCode.Int64 or SignatureTypeCode.UInt64=>CilStackKind.I8,
            SignatureTypeCode.Single or SignatureTypeCode.Double=>CilStackKind.Float,
            SignatureTypeCode.String or SignatureTypeCode.Object or SignatureTypeCode.SZArray or SignatureTypeCode.Array=>CilStackKind.ObjectReference,
            SignatureTypeCode.IntPtr or SignatureTypeCode.UIntPtr=>CilStackKind.NativeInt,
            SignatureTypeCode.ByReference or SignatureTypeCode.Pointer=>CilStackKind.ManagedPointer,
            _=>CilStackKind.I4
        };
    }

    private static CilStackKind AbiKind(GenericRepresentation representation)
        =>representation.RequiresSpecialization&&representation.Size==8?CilStackKind.I8:CilStackKind.NativeInt;

    private static GenericRepresentation FromTypeHandle(MetadataReader md,EntityHandle handle,CompilationModel model)
    {
        var name=MetadataLoader.ResolveTypeName(md,handle);
        if(name is null||!model.Types.TryGetValue(name,out var type)||!type.IsValueType)return new(GenericRepresentationKind.PointerSized,4);
        var size=Math.Max(1,type.InstanceSize);
        return size<=4?new(GenericRepresentationKind.PointerSized,size):new(GenericRepresentationKind.ValueType,size);
    }

    private static GenericRepresentation ReadGenericInstance(MetadataReader md,ref BlobReader reader,CompilationModel model)
    {
        if(reader.ReadSignatureTypeCode()!=SignatureTypeCode.TypeHandle)return new(GenericRepresentationKind.PointerSized,4);
        var definition=reader.ReadTypeHandle();var name=MetadataLoader.ResolveTypeName(md,definition);
        var isValue=name is not null&&model.Types.TryGetValue(name,out var type)&&type.IsValueType;
        var count=reader.ReadCompressedInteger();
        for(var i=0;i<count;i++)ReadRepresentation(md,ref reader,model);
        return isValue?new(GenericRepresentationKind.ValueType,Math.Max(1,model.Types[name!].InstanceSize)):new(GenericRepresentationKind.PointerSized,4);
    }
}
