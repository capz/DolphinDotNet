using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace DolphinDotNet.Compiler;

internal static class IlImporter
{
    public static IrMethod Import(PEReader pe, CompilationModel model, MethodModel method, DependencyGraph graph)
    {
        var md=model.Metadata; var def=md.GetMethodDefinition(method.Handle);
        if(def.RelativeVirtualAddress==0) throw new InvalidDataException($"{method.Key} has no body.");
        var body=pe.GetMethodBody(def.RelativeVirtualAddress);
        if(body.ExceptionRegions.Length!=0) throw new NotSupportedException($"Exception regions in {method.Key} are not supported yet.");
        var il=body.GetILBytes().ToArray(); var result=new List<IrInstruction>(); int p=0;
        graph.AddMethod(method.Key); graph.AddType(method.Key.TypeName);
        while(p<il.Length)
        {
            int offset=p; byte op=il[p++];
            switch(op)
            {
                case 0x00: break;
                case >=0x02 and <=0x05: result.Add(new IrLoadArg(op-0x02)); break;
                case 0x0e: Need(il,p,1,offset); result.Add(new IrLoadArg(il[p++])); break;
                case 0x15: result.Add(new IrConstI4(-1)); break;
                case >=0x16 and <=0x1e: result.Add(new IrConstI4(op-0x16)); break;
                case 0x1f: Need(il,p,1,offset); result.Add(new IrConstI4((sbyte)il[p++])); break;
                case 0x20: Need(il,p,4,offset); result.Add(new IrConstI4(BitConverter.ToInt32(il,p)));p+=4;break;
                case 0x58: result.Add(new IrAdd()); break;
                case 0x59: result.Add(new IrSub()); break;
                case 0x5a: result.Add(new IrMul()); break;
                case 0x28: ImportCall(md,model,graph,result,ReadToken(il,ref p,offset),false); break;
                case 0x6f: ImportCall(md,model,graph,result,ReadToken(il,ref p,offset),true); break;
                case 0x73:
                {
                    var token=ReadToken(il,ref p,offset); var target=ResolveMethod(md,model,token);
                    graph.AddType(target.Key.TypeName); graph.AddMethod(target.Key);
                    result.Add(new IrNewObject(target.Key.TypeName)); result.Add(new IrCall(target.Key,false)); break;
                }
                case 0x7b: { var f=ResolveField(md,model,ReadToken(il,ref p,offset));graph.AddType(f.DeclaringType);result.Add(new IrLoadField(f.DeclaringType,f.Name));break; }
                case 0x7d: { var f=ResolveField(md,model,ReadToken(il,ref p,offset));graph.AddType(f.DeclaringType);result.Add(new IrStoreField(f.DeclaringType,f.Name));break; }
                case 0x2a: result.Add(new IrReturn()); break;
                default: throw new NotSupportedException($"AOT importer: unsupported CIL opcode 0x{op:x2} in {method.Key} at IL_{offset:x4}.");
            }
        }
        return new IrMethod(method.Key,result);
    }

    private static void ImportCall(MetadataReader md,CompilationModel model,DependencyGraph graph,List<IrInstruction> result,EntityHandle token,bool virt)
    { var target=ResolveMethod(md,model,token); graph.AddMethod(target.Key);graph.AddType(target.Key.TypeName);result.Add(new IrCall(target.Key,virt)); }

    internal static MethodModel ResolveMethod(MetadataReader md,CompilationModel model,EntityHandle h)
    {
        if(h.Kind==HandleKind.MethodDefinition)
        {
            var d=md.GetMethodDefinition((MethodDefinitionHandle)h);var t=md.GetTypeDefinition(d.GetDeclaringType());
            return Find(model,Full(md.GetString(t.Namespace),md.GetString(t.Name)),md.GetString(d.Name));
        }
        if(h.Kind==HandleKind.MemberReference)
        {
            var m=md.GetMemberReference((MemberReferenceHandle)h);
            var type=MetadataLoader.ResolveTypeName(md,m.Parent) ?? throw new NotSupportedException($"Method parent {m.Parent.Kind}.");
            return Find(model,type,md.GetString(m.Name));
        }
        throw new NotSupportedException($"Method token {h.Kind}.");
    }
    private static MethodModel Find(CompilationModel model,string type,string name)=>model.Methods.TryGetValue(new MethodKey(type,name),out var m)?m:throw new NotSupportedException($"External/unmodeled method {type}::{name}.");
    private static FieldModel ResolveField(MetadataReader md,CompilationModel model,EntityHandle h)
    {
        string type,name;
        if(h.Kind==HandleKind.FieldDefinition){var f=md.GetFieldDefinition((FieldDefinitionHandle)h);var t=md.GetTypeDefinition(f.GetDeclaringType());type=Full(md.GetString(t.Namespace),md.GetString(t.Name));name=md.GetString(f.Name);}
        else if(h.Kind==HandleKind.MemberReference){var f=md.GetMemberReference((MemberReferenceHandle)h);type=MetadataLoader.ResolveTypeName(md,f.Parent)??throw new NotSupportedException("Field parent.");name=md.GetString(f.Name);}
        else throw new NotSupportedException($"Field token {h.Kind}.");
        return model.Fields.TryGetValue((type,name),out var field)?field:throw new NotSupportedException($"Unknown field {type}.{name}.");
    }
    private static EntityHandle ReadToken(byte[] il,ref int p,int offset){Need(il,p,4,offset);var h=MetadataTokens.EntityHandle(BitConverter.ToInt32(il,p));p+=4;return h;}
    private static void Need(byte[] il,int p,int n,int o){if(p+n>il.Length)throw new InvalidDataException($"Truncated CIL at IL_{o:x4}.");}
    private static string Full(string ns,string name)=>string.IsNullOrEmpty(ns)?name:ns+"."+name;
}
