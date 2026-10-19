using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
namespace DolphinDotNet.Compiler;
internal sealed record ShapedArrayInfo(int Rank,int Parameters,GenericRepresentation Element);
internal static class ShapedArrayBindings
{
    internal static ShapedArrayInfo? Read(MetadataReader md,CompilationModel model,CilInstruction instruction,MethodModel context)
    {
        if(instruction.Operand is not CilMetadataToken {Token:var raw}||MetadataTokens.EntityHandle(raw).Kind!=HandleKind.MemberReference)return null;
        var member=md.GetMemberReference((MemberReferenceHandle)MetadataTokens.EntityHandle(raw));
        if(member.Parent.Kind!=HandleKind.TypeSpecification)return null;
        var reader=md.GetBlobReader(md.GetTypeSpecification((TypeSpecificationHandle)member.Parent).Signature);
        if(reader.ReadSignatureTypeCode()!=SignatureTypeCode.Array)return null;
        var element=GenericSharing.ReadRepresentation(md,ref reader,model,context);int rank=reader.ReadCompressedInteger();
        var signature=md.GetBlobReader(member.Signature);signature.ReadSignatureHeader();int count=signature.ReadCompressedInteger();
        return new(rank,count,element);
    }
}
