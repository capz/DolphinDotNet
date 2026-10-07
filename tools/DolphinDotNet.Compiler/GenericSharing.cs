using System.Reflection.Metadata;

namespace DolphinDotNet.Compiler;

internal enum GenericRepresentationKind { PointerSized, ValueType }

internal readonly record struct GenericRepresentation(GenericRepresentationKind Kind,int Size)
{
    public bool RequiresSpecialization=>Kind==GenericRepresentationKind.ValueType&&Size>4;
    public string Key=>RequiresSpecialization?$"v{Size}":"p";
}

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

    public static string SpecializationSuffix(IReadOnlyList<GenericRepresentation> args)
        =>args.Any(x=>x.RequiresSpecialization)?"|g:"+string.Join(",",args.Select(x=>x.Key)):string.Empty;

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
