using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace DolphinDotNet.Compiler;

// Decode complete signatures: consuming just the leading type byte loses the
// next parameter after a generic instance, array, class, or byref parameter.
internal sealed class SignatureAbi(CompilationModel model) : ISignatureTypeProvider<SignatureAbi.Type, SignatureAbi.Context>
{
    internal sealed record Type(CilStackKind? Kind, int Size=0, IReadOnlyList<int>? References=null, string? Name=null,int ScalarSize=4,GenericRepresentation? Representation=null);
    internal sealed record Context(IReadOnlyList<GenericRepresentation> Types, IReadOnlyList<GenericRepresentation> Methods);
    private static readonly Type Reference = new(CilStackKind.ObjectReference,ScalarSize:8);
    public static GenericAbi Decode(MetadataReader md, MethodModel method, CompilationModel model,
        IReadOnlyList<GenericRepresentation> types, IReadOnlyList<GenericRepresentation> methods)
    {
        var sig=md.GetMethodDefinition(method.Handle).DecodeSignature(new SignatureAbi(model),new Context(types,methods));
        return new(sig.ParameterTypes.Select(t=>t.Kind??CilStackKind.Unknown).ToArray(),sig.ReturnType.Kind,
            sig.ParameterTypes.Select(t=>new LocalStorage(t.Size,t.Kind??CilStackKind.Unknown,t.References)).ToArray(),new LocalStorage(sig.ReturnType.Size,sig.ReturnType.Kind??CilStackKind.Unknown,sig.ReturnType.References));
    }
    public Type GetPrimitiveType(PrimitiveTypeCode code)=>Primitive(code) with { Name="System."+code };
    private Type Primitive(PrimitiveTypeCode code)=>code switch {
        PrimitiveTypeCode.Void=>new(null),
        PrimitiveTypeCode.Int64 or PrimitiveTypeCode.UInt64=>new(CilStackKind.I8,ScalarSize:8),
        PrimitiveTypeCode.Boolean or PrimitiveTypeCode.SByte or PrimitiveTypeCode.Byte=>new(CilStackKind.I4,ScalarSize:1),
        PrimitiveTypeCode.Char or PrimitiveTypeCode.Int16 or PrimitiveTypeCode.UInt16=>new(CilStackKind.I4,ScalarSize:2),
        PrimitiveTypeCode.Single=>new(CilStackKind.Float),
        PrimitiveTypeCode.Double=>new(CilStackKind.Float,ScalarSize:8),
        PrimitiveTypeCode.String or PrimitiveTypeCode.Object=>Reference,
        PrimitiveTypeCode.IntPtr or PrimitiveTypeCode.UIntPtr=>new(CilStackKind.NativeInt),
        _=>new(CilStackKind.I4)
    };
    public Type GetTypeFromDefinition(MetadataReader md,TypeDefinitionHandle h,byte raw)=>Named(MetadataLoader.ResolveTypeName(md,h),raw);
    public Type GetTypeFromReference(MetadataReader md,TypeReferenceHandle h,byte raw)=>Named(MetadataLoader.ResolveTypeName(md,h),raw);
    private Type Named(string? name,byte raw)
    {
        if(name is "System.IO.FileMode" or "System.IO.FileAccess" or "System.IO.FileShare" or "System.IO.SeekOrigin" or "System.IO.SearchOption" || name is not null && model.Types.TryGetValue(name,out var enumType)&&enumType.BaseType=="System.Enum")return new(CilStackKind.I4,Name:name);
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
        =>index>=args.Count?new(CilStackKind.NativeInt):args[index].IsReference?Reference with { Name=args[index].TypeName,Representation=args[index] }:args[index].IsAggregate?new(CilStackKind.ManagedPointer,args[index].Size,args[index].ReferenceOffsets,args[index].TypeName,Representation:args[index]):new(args[index].StackKind,Name:args[index].TypeName,ScalarSize:args[index].Size,Representation:args[index]);
    public Type GetGenericInstantiation(Type generic,ImmutableArray<Type> args)
    {
        if(generic.Name is {} definition&&model.Types.TryGetValue(definition,out var tm)&&tm.IsValueType&&args.All(t=>t.Representation is not null||t.Name is not null))
        {
            var reps=args.Select(t=>t.Representation??new GenericRepresentation(t.Kind==CilStackKind.ObjectReference?GenericRepresentationKind.PointerSized:GenericRepresentationKind.ValueType,t.Size>0?t.Size:t.ScalarSize,t.Kind==CilStackKind.ObjectReference?new[]{0}:t.References,t.Name)).ToArray();
            var closed=definition+GenericSharing.SpecializationSuffix(reps,Array.Empty<GenericRepresentation>());
            if(!model.Types.ContainsKey(closed))
            {
                var method=model.Methods.Values.First(m=>m.Key.TypeName==definition);
                model.Types[closed]=tm with { FullName=closed,GenericDefinition=definition,TypeArguments=reps };
                MetadataLoader.SpecializeFields(model,method,closed,reps);
            }
            var layout=model.Types[closed];var refs=model.Fields.Values.Where(f=>f.DeclaringType==closed&&!f.IsStatic).SelectMany(f=>f.IsReference?new[]{f.Offset}:(f.ReferenceOffsets??Array.Empty<int>()).Select(o=>f.Offset+o)).ToArray();
            return new(CilStackKind.ManagedPointer,layout.InstanceSize,refs,closed,Representation:new GenericRepresentation(GenericRepresentationKind.ValueType,layout.InstanceSize,refs,closed,reps));
        }
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
