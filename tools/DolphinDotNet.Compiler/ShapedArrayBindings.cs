using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
namespace DolphinDotNet.Compiler;
internal sealed record ShapedArrayInfo(int Rank,int Parameters,GenericRepresentation Element,bool Vector=false);
internal static class ShapedArrayBindings
{
    internal static ShapedArrayInfo? Read(MetadataReader md,CompilationModel model,CilInstruction instruction,MethodModel context)
    {
        if(instruction.Operand is not CilMetadataToken {Token:var raw})return null;
        var handle=MetadataTokens.EntityHandle(raw);
        if(handle.Kind==HandleKind.TypeSpecification && instruction.OpCode is 0x74 or 0x75)
        {
            var typeReader=md.GetBlobReader(md.GetTypeSpecification((TypeSpecificationHandle)handle).Signature);
            var code=typeReader.ReadSignatureTypeCode();
            if(code is not (SignatureTypeCode.Array or SignatureTypeCode.SZArray))return null;
            var item=GenericSharing.ReadRepresentation(md,ref typeReader,model,context);
            int dimensions=code==SignatureTypeCode.SZArray?1:typeReader.ReadCompressedInteger();
            return new(dimensions,0,item,code==SignatureTypeCode.SZArray);
        }
        if(handle.Kind!=HandleKind.MemberReference)return null;
        var member=md.GetMemberReference((MemberReferenceHandle)MetadataTokens.EntityHandle(raw));
        if(member.Parent.Kind!=HandleKind.TypeSpecification)return null;
        var reader=md.GetBlobReader(md.GetTypeSpecification((TypeSpecificationHandle)member.Parent).Signature);
        if(reader.ReadSignatureTypeCode()!=SignatureTypeCode.Array)return null;
        var element=GenericSharing.ReadRepresentation(md,ref reader,model,context);int rank=reader.ReadCompressedInteger();
        var signature=md.GetBlobReader(member.Signature);signature.ReadSignatureHeader();int count=signature.ReadCompressedInteger();
        return new(rank,count,element);
    }
}
