using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace DolphinDotNet.Compiler;

// Decode complete signatures: consuming just the leading type byte loses the
// next parameter after a generic instance, array, class, or byref parameter.
internal sealed class SignatureAbi(CompilationModel model) : ISignatureTypeProvider<SignatureAbi.Type, SignatureAbi.Context>
{
    internal sealed record Type(CilStackKind? Kind, int Size=0, IReadOnlyList<int>? References=null, string? Name=null,int ScalarSize=4);
    internal sealed record Context(IReadOnlyList<GenericRepresentation> Types, IReadOnlyList<GenericRepresentation> Methods);
    private static readonly Type Reference = new(CilStackKind.ObjectReference,ScalarSize:8);
    public static GenericAbi Decode(MetadataReader md, MethodModel method, CompilationModel model,
        IReadOnlyList<GenericRepresentation> types, IReadOnlyList<GenericRepresentation> methods)
    {
        var sig=md.GetMethodDefinition(method.Handle).DecodeSignature(new SignatureAbi(model),new Context(types,methods));
        return new(sig.ParameterTypes.Select(t=>t.Kind??CilStackKind.Unknown).ToArray(),sig.ReturnType.Kind,
            sig.ParameterTypes.Select(t=>new LocalStorage(t.Size,t.Kind??CilStackKind.Unknown,t.References)).ToArray());
    }
    public Type GetPrimitiveType(PrimitiveTypeCode code)=>code switch {
        PrimitiveTypeCode.Void=>new(null),
        PrimitiveTypeCode.Int64 or PrimitiveTypeCode.UInt64=>new(CilStackKind.I8,ScalarSize:8),
        PrimitiveTypeCode.Boolean or PrimitiveTypeCode.SByte or PrimitiveTypeCode.Byte=>new(CilStackKind.I4,ScalarSize:1),
        PrimitiveTypeCode.Char or PrimitiveTypeCode.Int16 or PrimitiveTypeCode.UInt16=>new(CilStackKind.I4,ScalarSize:2),
        PrimitiveTypeCode.Single or PrimitiveTypeCode.Double=>new(CilStackKind.Float),
        PrimitiveTypeCode.String or PrimitiveTypeCode.Object=>Reference,
        PrimitiveTypeCode.IntPtr or PrimitiveTypeCode.UIntPtr=>new(CilStackKind.NativeInt),
        _=>new(CilStackKind.I4)
    };
    public Type GetTypeFromDefinition(MetadataReader md,TypeDefinitionHandle h,byte raw)=>Named(MetadataLoader.ResolveTypeName(md,h),raw);
    public Type GetTypeFromReference(MetadataReader md,TypeReferenceHandle h,byte raw)=>Named(MetadataLoader.ResolveTypeName(md,h),raw);
    private Type Named(string? name,byte raw)
    {
        if(raw==0x11 && name is not null && model.Types.TryGetValue(name,out var t))
            return new(CilStackKind.ManagedPointer,Math.Max(1,t.InstanceSize),model.Fields.Values.Where(f=>f.DeclaringType==name&&!f.IsStatic&&f.IsReference).Select(f=>f.Offset).ToArray(),name);
        return Reference with { Name=name };
    }
    public Type GetTypeFromSpecification(MetadataReader md,Context context,TypeSpecificationHandle h,byte raw)=>md.GetTypeSpecification(h).DecodeSignature(this,context);
    public Type GetSZArrayType(Type element)=>Reference;
    public Type GetArrayType(Type element,ArrayShape shape)=>Reference;
    public Type GetByReferenceType(Type element)=>new(CilStackKind.ManagedPointer);
    public Type GetPointerType(Type element)=>new(CilStackKind.ManagedPointer);
    public Type GetFunctionPointerType(MethodSignature<Type> signature)=>new(CilStackKind.NativeInt);
    public Type GetPinnedType(Type element)=>element;
    public Type GetModifiedType(Type modifier,Type element,bool required)=>element;
    public Type GetGenericMethodParameter(Context context,int index)=>Argument(context.Methods,index);
    public Type GetGenericTypeParameter(Context context,int index)=>Argument(context.Types,index);
    private static Type Argument(IReadOnlyList<GenericRepresentation> args,int index)
        =>index>=args.Count?new(CilStackKind.NativeInt):args[index].ContainsReferences?Reference:new(args[index].Size==8?CilStackKind.I8:CilStackKind.NativeInt,ScalarSize:args[index].Size);
    public Type GetGenericInstantiation(Type generic,ImmutableArray<Type> args)
    {
        var sizes=args.Select(t=>t.Size>0?t.Size:t.ScalarSize).ToArray();
        if(generic.Name=="System.Nullable`1")return new(CilStackKind.ManagedPointer,4+((sizes[0]+3)&~3));
        if(generic.Name=="System.Collections.Generic.KeyValuePair`2") {
            var alignment=Math.Min(Math.Max(sizes[1],1),8);var second=(sizes[0]+alignment-1)&~(alignment-1);
            var refs=args.SelectMany((t,i)=>t.Kind==CilStackKind.ObjectReference?new[]{i==0?0:second}:(t.References??Array.Empty<int>()).Select(o=>o+(i==0?0:second))).ToArray();
            return new(CilStackKind.ManagedPointer,second+sizes[1],refs);
        }
        if(generic.Name=="System.ArraySegment`1")return new(CilStackKind.ManagedPointer,16,new[]{0});
        if(generic.Name=="System.ArraySegment`1+Enumerator")return new(CilStackKind.ManagedPointer,24,new[]{0});
        return generic;
    }
}
