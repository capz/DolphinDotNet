using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace DolphinDotNet.Compiler;

// Signature blobs contain assembly-local type tokens. Compare resolved types when
// matching a game assembly's override to a framework assembly's virtual slot.
internal sealed class MethodSignatureIdentity : ISignatureTypeProvider<string, object?>
{
    internal static bool Matches(MethodModel left, MethodModel right, CompilationModel model)
    {
        if (left.Key.Name!=right.Key.Name || left.ParameterCount!=right.ParameterCount) return false;
        if (left.AssemblyName==right.AssemblyName && left.Key.Signature==right.Key.Signature) return true;
        if (left.Handle.IsNil || right.Handle.IsNil) return false;
        var provider=new MethodSignatureIdentity();
        var a=model.Assemblies[left.AssemblyName].Metadata.GetMethodDefinition(left.Handle)
            .DecodeSignature(provider, null);
        var b=model.Assemblies[right.AssemblyName].Metadata.GetMethodDefinition(right.Handle)
            .DecodeSignature(provider, null);
        return a.Header.RawValue==b.Header.RawValue && a.GenericParameterCount==b.GenericParameterCount
            && a.RequiredParameterCount==b.RequiredParameterCount && a.ReturnType==b.ReturnType
            && a.ParameterTypes.SequenceEqual(b.ParameterTypes);
    }
    public string GetPrimitiveType(PrimitiveTypeCode code) => code.ToString();
    public string GetTypeFromDefinition(MetadataReader md, TypeDefinitionHandle h, byte raw)
        => raw+":"+MetadataLoader.ResolveTypeName(md,h);
    public string GetTypeFromReference(MetadataReader md, TypeReferenceHandle h, byte raw)
        => raw+":"+MetadataLoader.ResolveTypeName(md,h);
    public string GetTypeFromSpecification(MetadataReader md, object? context, TypeSpecificationHandle h, byte raw)
        => md.GetTypeSpecification(h).DecodeSignature(this,context);
    public string GetSZArrayType(string element) => element+"[]";
    public string GetArrayType(string element, ArrayShape shape)
        => element+"["+shape.Rank+";"+string.Join(",",shape.Sizes)+";"+string.Join(",",shape.LowerBounds)+"]";
    public string GetByReferenceType(string element) => element+"&";
    public string GetPointerType(string element) => element+"*";
    public string GetFunctionPointerType(MethodSignature<string> signature)
        => "fn:"+signature.Header.RawValue+":"+signature.ReturnType+"("+string.Join(",",signature.ParameterTypes)+")";
    public string GetPinnedType(string element) => element+" pinned";
    public string GetModifiedType(string modifier, string element, bool required)
        => element+(required ? " modreq(" : " modopt(")+modifier+")";
    public string GetGenericMethodParameter(object? context, int index) => "!!"+index;
    public string GetGenericTypeParameter(object? context, int index) => "!"+index;
    public string GetGenericInstantiation(string generic, ImmutableArray<string> args)
        => generic+"<"+string.Join(",",args)+">";
}
