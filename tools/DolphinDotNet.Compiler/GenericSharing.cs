using System.Reflection.Metadata;

namespace DolphinDotNet.Compiler;

internal enum GenericRepresentationKind { PointerSized, ValueType }

internal readonly record struct GenericRepresentation(GenericRepresentationKind Kind,int Size,IReadOnlyList<int>? ReferenceOffsets=null)
{
    public bool RequiresSpecialization=>Kind==GenericRepresentationKind.ValueType&&Size>4;
    public bool ContainsReferences=>ReferenceOffsets is { Count: >0 };
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
            SignatureTypeCode.String or SignatureTypeCode.Object or SignatureTypeCode.SZArray or SignatureTypeCode.Array=>new(GenericRepresentationKind.PointerSized,8,new[]{0}),
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
        if(definition=="System.Nullable`1"&&args.Length==1)return NullableLayout(args[0]).Size;
        if(definition=="System.Collections.Generic.KeyValuePair`2"&&args.Length==2)return PairLayout(args[0],args[1]).Size;
        if(definition=="System.ArraySegment`1"&&args.Length==1)return 16;
        if(definition=="System.ArraySegment`1+Enumerator"&&args.Length==1)return 24;
        if(definition is not null&&model.Types.TryGetValue(definition,out var type)&&type.IsValueType)return Math.Max(1,type.InstanceSize);
        return 0;
    }

    public static (int Size,IReadOnlyList<int> References) ReadGenericLocalLayout(MetadataReader md,ref BlobReader reader,CompilationModel model)
    {
        if(reader.ReadSignatureTypeCode()!=SignatureTypeCode.GenericTypeInstance||reader.ReadSignatureTypeCode()!=SignatureTypeCode.TypeHandle)return (0,Array.Empty<int>());
        var definition=MetadataLoader.ResolveTypeName(md,reader.ReadTypeHandle());var count=reader.ReadCompressedInteger();var args=new GenericRepresentation[count];for(var i=0;i<count;i++)args[i]=ReadRepresentation(md,ref reader,model);
        if(definition=="System.Nullable`1"&&args.Length==1)return NullableLayout(args[0]);
        if(definition=="System.Collections.Generic.KeyValuePair`2"&&args.Length==2)return PairLayout(args[0],args[1]);
        if(definition=="System.ArraySegment`1"&&args.Length==1)return (16,new[]{0});
        if(definition=="System.ArraySegment`1+Enumerator"&&args.Length==1)return (24,new[]{0});
        if(definition is not null&&model.Types.TryGetValue(definition,out var type)&&type.IsValueType)return (Math.Max(1,type.InstanceSize),model.Fields.Values.Where(f=>f.DeclaringType==definition&&f.IsReference&&!f.IsStatic).Select(f=>f.Offset).ToArray());
        return (0,Array.Empty<int>());
    }

    private static (int Size,IReadOnlyList<int> References) NullableLayout(GenericRepresentation arg)=> (4+Align4(arg.Size), (arg.ReferenceOffsets??Array.Empty<int>()).Select(x=>4+x).ToArray());
    private static (int Size,IReadOnlyList<int> References) PairLayout(GenericRepresentation a,GenericRepresentation b){var second=Align(a.Size,Math.Min(Math.Max(b.Size,1),4));var refs=(a.ReferenceOffsets??Array.Empty<int>()).Concat((b.ReferenceOffsets??Array.Empty<int>()).Select(x=>second+x)).ToArray();return(second+b.Size,refs);}
    private static int Align4(int value)=>(value+3)&~3;
    private static int Align(int value,int alignment)=>(value+alignment-1)&~(alignment-1);

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

    public static GenericAbi BuildAbi(MetadataReader md,MethodModel definition,IReadOnlyList<GenericRepresentation> typeArgs,IReadOnlyList<GenericRepresentation> methodArgs,CompilationModel model)
    {
        return SignatureAbi.Decode(md,definition,model,typeArgs,methodArgs);
    }

    private static GenericRepresentation FromTypeHandle(MetadataReader md,EntityHandle handle,CompilationModel model)
    {
        var name=MetadataLoader.ResolveTypeName(md,handle);
        if(name is null||!model.Types.TryGetValue(name,out var type)||!type.IsValueType)return new(GenericRepresentationKind.PointerSized,8,new[]{0});
        var size=Math.Max(1,type.InstanceSize);
        return size<=4?new(GenericRepresentationKind.PointerSized,size):new(GenericRepresentationKind.ValueType,size);
    }

    private static GenericRepresentation ReadGenericInstance(MetadataReader md,ref BlobReader reader,CompilationModel model)
    {
        if(reader.ReadSignatureTypeCode()!=SignatureTypeCode.TypeHandle)return new(GenericRepresentationKind.PointerSized,4);
        var definition=reader.ReadTypeHandle();var name=MetadataLoader.ResolveTypeName(md,definition);
        var isValue=name is not null&&model.Types.TryGetValue(name,out var type)&&type.IsValueType;
        var count=reader.ReadCompressedInteger();
        var args=new GenericRepresentation[count];for(var i=0;i<count;i++)args[i]=ReadRepresentation(md,ref reader,model);
        if(name=="System.Nullable`1"&&args.Length==1){var l=NullableLayout(args[0]);return new(GenericRepresentationKind.ValueType,l.Size,l.References);}
        if(name=="System.Collections.Generic.KeyValuePair`2"&&args.Length==2){var l=PairLayout(args[0],args[1]);return new(GenericRepresentationKind.ValueType,l.Size,l.References);}
        if(name=="System.ArraySegment`1"&&args.Length==1)return new(GenericRepresentationKind.ValueType,16,new[]{0});
        if(name=="System.ArraySegment`1+Enumerator"&&args.Length==1)return new(GenericRepresentationKind.ValueType,24,new[]{0});
        return isValue?new(GenericRepresentationKind.ValueType,Math.Max(1,model.Types[name!].InstanceSize)):new(GenericRepresentationKind.PointerSized,8,new[]{0});
    }
}
