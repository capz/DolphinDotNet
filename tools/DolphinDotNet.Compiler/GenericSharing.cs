using System.Reflection.Metadata;

namespace DolphinDotNet.Compiler;

internal enum GenericRepresentationKind { PointerSized, ValueType }

internal readonly record struct GenericRepresentation(GenericRepresentationKind Kind,int Size,IReadOnlyList<int>? ReferenceOffsets=null,string? TypeName=null,IReadOnlyList<GenericRepresentation>? Arguments=null)
{
    public bool RequiresSpecialization=>Kind==GenericRepresentationKind.ValueType&&Size>4;
    public bool ContainsReferences=>ReferenceOffsets is { Count: >0 };
    public bool IsReference=>Kind==GenericRepresentationKind.PointerSized&&ContainsReferences;
    public bool IsAggregate=>Kind==GenericRepresentationKind.ValueType&&TypeName is not ("System.Boolean" or "System.Byte" or "System.SByte" or "System.Char" or "System.Int16" or "System.UInt16" or "System.Int32" or "System.UInt32" or "System.Int64" or "System.UInt64" or "System.Single" or "System.Double" or "System.IntPtr" or "System.UIntPtr");
    public CilStackKind StackKind=>TypeName is "System.Single" or "System.Double"?CilStackKind.Float:IsReference?CilStackKind.ObjectReference:IsAggregate?CilStackKind.ManagedPointer:Size==8?CilStackKind.I8:CilStackKind.I4;
    public string Key { get { var shape=IsReference?"r":RequiresSpecialization?$"v{Size}":"p";return TypeName is null or "System.Int32" or "System.Object"?shape:shape+"_"+TypeName.Replace('.','_').Replace('`','_').Replace('+','_')+(Arguments is { Count:>0 }?"_"+string.Join("_",Arguments.Select(a=>a.Key)):""); } }
}

// GameCube is 32-bit: only representations wider than one machine word need a distinct body.
internal static class GenericSharing
{
    public static IReadOnlyList<GenericRepresentation> ReadMethodArguments(MetadataReader md,MethodSpecificationHandle handle,CompilationModel model,MethodModel? context=null)
    {
        var spec=md.GetMethodSpecification(handle);var reader=md.GetBlobReader(spec.Signature);
        var header=reader.ReadSignatureHeader();
        if(header.Kind!=SignatureKind.MethodSpecification)throw new InvalidDataException("Invalid generic method specification.");
        var count=reader.ReadCompressedInteger();var result=new GenericRepresentation[count];
        for(var i=0;i<count;i++)result[i]=ReadRepresentation(md,ref reader,model,context);
        return result;
    }

    public static GenericRepresentation ReadRepresentation(MetadataReader md,ref BlobReader reader,CompilationModel model,MethodModel? context=null)
    {
        var code=reader.ReadSignatureTypeCode();
        if(code is SignatureTypeCode.Boolean or SignatureTypeCode.Byte or SignatureTypeCode.SByte or SignatureTypeCode.Char or SignatureTypeCode.Int16 or SignatureTypeCode.UInt16 or SignatureTypeCode.Int32 or SignatureTypeCode.UInt32 or SignatureTypeCode.Single or SignatureTypeCode.IntPtr or SignatureTypeCode.UIntPtr or SignatureTypeCode.Int64 or SignatureTypeCode.UInt64 or SignatureTypeCode.Double or SignatureTypeCode.String or SignatureTypeCode.Object)
        {
            var name="System."+code;
            var size=code is SignatureTypeCode.Boolean or SignatureTypeCode.Byte or SignatureTypeCode.SByte?1:code is SignatureTypeCode.Char or SignatureTypeCode.Int16 or SignatureTypeCode.UInt16?2:code is SignatureTypeCode.Int64 or SignatureTypeCode.UInt64 or SignatureTypeCode.Double or SignatureTypeCode.String or SignatureTypeCode.Object?8:4;
            return new(size==8&&code is not (SignatureTypeCode.String or SignatureTypeCode.Object)?GenericRepresentationKind.ValueType:GenericRepresentationKind.PointerSized,size,code is SignatureTypeCode.String or SignatureTypeCode.Object?new[]{0}:null,name);
        }
        return code switch
        {
            SignatureTypeCode.SZArray=>ReadArray(md,ref reader,model,context),
            SignatureTypeCode.TypeHandle=>FromTypeHandle(md,reader.ReadTypeHandle(),model),
            SignatureTypeCode.GenericTypeInstance=>ReadGenericInstance(md,ref reader,model,context),
            SignatureTypeCode.GenericTypeParameter=>ContextArgument(context?.TypeArguments,reader.ReadCompressedInteger()),
            SignatureTypeCode.GenericMethodParameter=>ContextArgument(context?.MethodArguments,reader.ReadCompressedInteger()),
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
    private static (int Size,IReadOnlyList<int> References) PairLayout(GenericRepresentation a,GenericRepresentation b){var second=Align(a.Size,Math.Min(Math.Max(b.Size,1),8));var refs=(a.ReferenceOffsets??Array.Empty<int>()).Concat((b.ReferenceOffsets??Array.Empty<int>()).Select(x=>second+x)).ToArray();return(second+b.Size,refs);}
    private static int Align4(int value)=>(value+3)&~3;
    private static int Align(int value,int alignment)=>(value+alignment-1)&~(alignment-1);

    public static string SpecializationSuffix(IReadOnlyList<GenericRepresentation> typeArgs,IReadOnlyList<GenericRepresentation> methodArgs)
    {
        var all=typeArgs.Concat(methodArgs).ToArray();
        return all.Length>0?"|g:"+string.Join(",",all.Select(x=>x.Key)):string.Empty;
    }

    public static IReadOnlyList<GenericRepresentation> ReadTypeArguments(MetadataReader md,TypeSpecificationHandle handle,CompilationModel model,MethodModel? context=null)
    {
        var reader=md.GetBlobReader(md.GetTypeSpecification(handle).Signature);
        if(reader.ReadSignatureTypeCode()!=SignatureTypeCode.GenericTypeInstance||reader.ReadSignatureTypeCode()!=SignatureTypeCode.TypeHandle)return Array.Empty<GenericRepresentation>();
        reader.ReadTypeHandle();var count=reader.ReadCompressedInteger();var result=new GenericRepresentation[count];
        for(var i=0;i<count;i++)result[i]=ReadRepresentation(md,ref reader,model,context);
        return result;
    }

    public static GenericAbi BuildAbi(MetadataReader md,MethodModel definition,IReadOnlyList<GenericRepresentation> typeArgs,IReadOnlyList<GenericRepresentation> methodArgs,CompilationModel model)
    {
        return SignatureAbi.Decode(md,definition,model,typeArgs,methodArgs);
    }

    private static GenericRepresentation ReadArray(MetadataReader md,ref BlobReader reader,CompilationModel model,MethodModel? context)
    { ReadRepresentation(md,ref reader,model,context);return new(GenericRepresentationKind.PointerSized,8,new[]{0}); }

    private static GenericRepresentation ContextArgument(IReadOnlyList<GenericRepresentation>? args,int index)
        =>args is not null&&index<args.Count?args[index]:new(GenericRepresentationKind.PointerSized,4);

    private static GenericRepresentation FromTypeHandle(MetadataReader md,EntityHandle handle,CompilationModel model)
    {
        var name=MetadataLoader.ResolveTypeName(md,handle);
        if(name is null||!model.Types.TryGetValue(name,out var type)||!type.IsValueType)return new(GenericRepresentationKind.PointerSized,8,new[]{0},name);
        var size=Math.Max(1,type.InstanceSize);
        if(type.BaseType=="System.Enum")return new(GenericRepresentationKind.PointerSized,size,null,name);
        return new(GenericRepresentationKind.ValueType,size,model.Fields.Values.Where(f=>f.DeclaringType==name&&!f.IsStatic&&f.IsReference).Select(f=>f.Offset).ToArray(),name);
    }

    private static GenericRepresentation ReadGenericInstance(MetadataReader md,ref BlobReader reader,CompilationModel model,MethodModel? context=null)
    {
        if(reader.ReadSignatureTypeCode()!=SignatureTypeCode.TypeHandle)return new(GenericRepresentationKind.PointerSized,4);
        var definition=reader.ReadTypeHandle();var name=MetadataLoader.ResolveTypeName(md,definition);
        var isValue=name is not null&&model.Types.TryGetValue(name,out var type)&&type.IsValueType;
        var count=reader.ReadCompressedInteger();
        var args=new GenericRepresentation[count];for(var i=0;i<count;i++)args[i]=ReadRepresentation(md,ref reader,model,context);
        if(name=="System.Nullable`1"&&args.Length==1){var l=NullableLayout(args[0]);return new(GenericRepresentationKind.ValueType,l.Size,l.References,name,args);}
        if(name=="System.Collections.Generic.KeyValuePair`2"&&args.Length==2){var l=PairLayout(args[0],args[1]);return new(GenericRepresentationKind.ValueType,l.Size,l.References,name,args);}
        if(name=="System.ArraySegment`1"&&args.Length==1)return new(GenericRepresentationKind.ValueType,16,new[]{0});
        if(name=="System.ArraySegment`1+Enumerator"&&args.Length==1)return new(GenericRepresentationKind.ValueType,24,new[]{0});
        if(name is not null&&model.Types.TryGetValue(name,out var tm)&&!tm.IsInterface){
            var method=model.Methods.Values.FirstOrDefault(m=>m.Key.TypeName==name&&!m.Handle.IsNil);
            if(method is not null){
                var closed=IlImporter.Specialize(md,model,method,args,Array.Empty<GenericRepresentation>()).Key.TypeName;
                var layout=model.Types[closed];var refs=model.Fields.Values.Where(f=>f.DeclaringType==closed&&!f.IsStatic).SelectMany(f=>f.IsReference?new[]{f.Offset}:(f.ReferenceOffsets??Array.Empty<int>()).Select(o=>f.Offset+o)).ToArray();
                return isValue?new(GenericRepresentationKind.ValueType,Math.Max(1,layout.InstanceSize),refs,closed,args):new(GenericRepresentationKind.PointerSized,8,new[]{0},closed,args);
            }
        }
        return isValue?new(GenericRepresentationKind.ValueType,Math.Max(1,model.Types[name!].InstanceSize),null,name,args):new(GenericRepresentationKind.PointerSized,8,new[]{0},name,args);
    }
}
