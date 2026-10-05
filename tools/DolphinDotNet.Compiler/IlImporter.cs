using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
namespace DolphinDotNet.Compiler;
internal static class IlImporter{
 public static IrMethod Import(PEReader pe,CompilationModel model,MethodModel method,DependencyGraph graph){
  var md=model.Assemblies[method.AssemblyName].Metadata;var def=md.GetMethodDefinition(method.Handle);if(def.RelativeVirtualAddress==0)throw new InvalidDataException($"{method.Key} has no body.");var body=pe.GetMethodBody(def.RelativeVirtualAddress);if(body.ExceptionRegions.Length!=0)throw new NotSupportedException($"Exception regions in {method.Key} are not supported yet.");
  int locals=body.LocalSignature.IsNil?0:ReadLocalCount(md,body.LocalSignature);var il=(body.GetILBytes() ?? throw new InvalidDataException("Method body has no IL bytes.")).ToArray();var result=new List<IrInstruction>();int p=0;graph.AddMethod(method.Key);graph.AddType(method.Key.TypeName);
  while(p<il.Length){int off=p;result.Add(new IrLabel(off));byte op=il[p++];if(op==0xfe){Need(il,p,1,off);byte ext=il[p++];switch(ext){case 0x01:result.Add(new IrCompareEqual());break;case 0x02:result.Add(new IrCompareGreaterThan());break;default:throw new NotSupportedException($"AOT importer: unsupported CIL opcode 0xfe{ext:x2} in {method.Key} at IL_{off:x4}.");}continue;}switch(op){
   case 0x00:break;case >=0x02 and <=0x05:result.Add(new IrLoadArg(op-2));break;
   case >=0x06 and <=0x09:result.Add(new IrLoadLocal(op-6));break;case >=0x0a and <=0x0d:result.Add(new IrStoreLocal(op-0x0a));break;
   case 0x0e:Need(il,p,1,off);result.Add(new IrLoadArg(il[p++]));break;case 0x10:Need(il,p,1,off);result.Add(new IrStoreArg(il[p++]));break;case 0x11:Need(il,p,1,off);result.Add(new IrLoadLocal(il[p++]));break;case 0x13:Need(il,p,1,off);result.Add(new IrStoreLocal(il[p++]));break;
   case 0x15:result.Add(new IrConstI4(-1));break;case >=0x16 and <=0x1e:result.Add(new IrConstI4(op-0x16));break;case 0x1f:Need(il,p,1,off);result.Add(new IrConstI4((sbyte)il[p++]));break;case 0x20:Need(il,p,4,off);result.Add(new IrConstI4(BitConverter.ToInt32(il,p)));p+=4;break;
   case 0x25:result.Add(new IrDup());break;case 0x26:result.Add(new IrPop());break;
   case 0x2b:result.Add(new IrBranch(ReadShortBranchTarget(il,ref p,off),IrBranchCondition.Always));break;
   case 0x2c:result.Add(new IrBranch(ReadShortBranchTarget(il,ref p,off),IrBranchCondition.False));break;
   case 0x2d:result.Add(new IrBranch(ReadShortBranchTarget(il,ref p,off),IrBranchCondition.True));break;
   case 0x2e:result.Add(new IrBranch(ReadShortBranchTarget(il,ref p,off),IrBranchCondition.Equal));break;
   case 0x2f:result.Add(new IrBranch(ReadShortBranchTarget(il,ref p,off),IrBranchCondition.GreaterOrEqual));break;
   case 0x30:result.Add(new IrBranch(ReadShortBranchTarget(il,ref p,off),IrBranchCondition.GreaterThan));break;
   case 0x31:result.Add(new IrBranch(ReadShortBranchTarget(il,ref p,off),IrBranchCondition.LessOrEqual));break;
   case 0x32:result.Add(new IrBranch(ReadShortBranchTarget(il,ref p,off),IrBranchCondition.LessThan));break;
   case 0x33:result.Add(new IrBranch(ReadShortBranchTarget(il,ref p,off),IrBranchCondition.NotEqual,true));break;
   case 0x34:result.Add(new IrBranch(ReadShortBranchTarget(il,ref p,off),IrBranchCondition.GreaterOrEqual,true));break;
   case 0x35:result.Add(new IrBranch(ReadShortBranchTarget(il,ref p,off),IrBranchCondition.GreaterThan,true));break;
   case 0x36:result.Add(new IrBranch(ReadShortBranchTarget(il,ref p,off),IrBranchCondition.LessOrEqual,true));break;
   case 0x37:result.Add(new IrBranch(ReadShortBranchTarget(il,ref p,off),IrBranchCondition.LessThan,true));break;
   case 0x38:result.Add(new IrBranch(ReadLongBranchTarget(il,ref p,off),IrBranchCondition.Always));break;
   case 0x39:result.Add(new IrBranch(ReadLongBranchTarget(il,ref p,off),IrBranchCondition.False));break;
   case 0x3a:result.Add(new IrBranch(ReadLongBranchTarget(il,ref p,off),IrBranchCondition.True));break;
   case 0x3b:result.Add(new IrBranch(ReadLongBranchTarget(il,ref p,off),IrBranchCondition.Equal));break;
   case 0x3c:result.Add(new IrBranch(ReadLongBranchTarget(il,ref p,off),IrBranchCondition.GreaterOrEqual));break;
   case 0x3d:result.Add(new IrBranch(ReadLongBranchTarget(il,ref p,off),IrBranchCondition.GreaterThan));break;
   case 0x3e:result.Add(new IrBranch(ReadLongBranchTarget(il,ref p,off),IrBranchCondition.LessOrEqual));break;
   case 0x3f:result.Add(new IrBranch(ReadLongBranchTarget(il,ref p,off),IrBranchCondition.LessThan));break;
   case 0x40:result.Add(new IrBranch(ReadLongBranchTarget(il,ref p,off),IrBranchCondition.NotEqual,true));break;
   case 0x41:result.Add(new IrBranch(ReadLongBranchTarget(il,ref p,off),IrBranchCondition.GreaterOrEqual,true));break;
   case 0x42:result.Add(new IrBranch(ReadLongBranchTarget(il,ref p,off),IrBranchCondition.GreaterThan,true));break;
   case 0x43:result.Add(new IrBranch(ReadLongBranchTarget(il,ref p,off),IrBranchCondition.LessOrEqual,true));break;
   case 0x44:result.Add(new IrBranch(ReadLongBranchTarget(il,ref p,off),IrBranchCondition.LessThan,true));break;
   case 0x45:{Need(il,p,4,off);var n=BitConverter.ToInt32(il,p);p+=4;if(n<0)throw new InvalidDataException($"Invalid switch at IL_{off:x4}.");Need(il,p,n*4,off);var baseOffset=p+n*4;var targets=new int[n];for(var si=0;si<n;si++){targets[si]=baseOffset+BitConverter.ToInt32(il,p);p+=4;}result.Add(new IrSwitch(targets));break;}
   case 0x58:result.Add(new IrAdd());break;case 0x59:result.Add(new IrSub());break;case 0x5a:result.Add(new IrMul());break;
   case 0x72:{Need(il,p,4,off);int raw=BitConverter.ToInt32(il,p);p+=4;var token=MetadataTokens.UserStringHandle(raw&0x00ffffff);result.Add(new IrLoadString(md.GetUserString(token)));break;}
   case 0x28:ImportCall(md,model,graph,result,ReadToken(il,ref p,off),false);break;case 0x6f:ImportCall(md,model,graph,result,ReadToken(il,ref p,off),true);break;
   case 0x73:{var target=ResolveMethod(md,model,ReadToken(il,ref p,off));graph.AddType(target.Key.TypeName);graph.AddMethod(target.Key);result.Add(new IrNewObject(target.Key.TypeName,target.Key,target.ParameterCount));break;}
   case 0x7b:{var f=ResolveField(md,model,ReadToken(il,ref p,off));graph.AddType(f.DeclaringType);result.Add(new IrLoadField(f.DeclaringType,f.Name));break;}case 0x7d:{var f=ResolveField(md,model,ReadToken(il,ref p,off));graph.AddType(f.DeclaringType);result.Add(new IrStoreField(f.DeclaringType,f.Name));break;}
   case 0x2a:result.Add(new IrReturn(method.ReturnsValue));break;default:throw new NotSupportedException($"AOT importer: unsupported CIL opcode 0x{op:x2} in {method.Key} at IL_{off:x4}.");}}
  return new IrMethod(method.Key,result,locals,method.ParameterCount,!method.IsStatic,method.ReturnsValue);
 }
 private static void ImportCall(MetadataReader md,CompilationModel model,DependencyGraph graph,List<IrInstruction> r,EntityHandle token,bool virt){if(TryIgnoreObjectCtor(md,token)){r.Add(new IrPop());return;}if(TryImportIntrinsic(md,token,r))return;var t=ResolveMethod(md,model,token);graph.AddMethod(t.Key);graph.AddType(t.Key.TypeName);r.Add(new IrCall(t.Key,virt,t.ParameterCount,!t.IsStatic,t.ReturnsValue));}
 private static bool TryImportIntrinsic(MetadataReader md,EntityHandle h,List<IrInstruction> r){switch(IntrinsicRegistry.Classify(md,h)){case IntrinsicKind.StringLength:r.Add(new IrStringLength());return true;case IntrinsicKind.GameCubeWriteLine:r.Add(new IrConsoleWriteLine());return true;case IntrinsicKind.GameCubeReadButtonsDown:r.Add(new IrReadButtonsDown());return true;default:return false;}}
 internal static bool TryIgnoreObjectCtor(MetadataReader md,EntityHandle h)=>IntrinsicRegistry.Classify(md,h)==IntrinsicKind.ObjectConstructor;
 internal static MethodModel ResolveMethod(MetadataReader md,CompilationModel model,EntityHandle h){if(h.Kind==HandleKind.MethodDefinition){var d=md.GetMethodDefinition((MethodDefinitionHandle)h);var t=md.GetTypeDefinition(d.GetDeclaringType());return Find(model,Full(md.GetString(t.Namespace),md.GetString(t.Name)),md.GetString(d.Name));}if(h.Kind==HandleKind.MemberReference){var m=md.GetMemberReference((MemberReferenceHandle)h);return Find(model,MetadataLoader.ResolveTypeName(md,m.Parent)??throw new NotSupportedException("Method parent."),md.GetString(m.Name));}throw new NotSupportedException($"Method token {h.Kind}.");}
 private static MethodModel Find(CompilationModel m,string t,string n)=>m.Methods.TryGetValue(new MethodKey(t,n),out var x)?x:throw new NotSupportedException($"External/unmodeled method {t}::{n}.");
 internal static FieldModel ResolveField(MetadataReader md,CompilationModel m,EntityHandle h){string t,n;if(h.Kind==HandleKind.FieldDefinition){var f=md.GetFieldDefinition((FieldDefinitionHandle)h);var td=md.GetTypeDefinition(f.GetDeclaringType());t=Full(md.GetString(td.Namespace),md.GetString(td.Name));n=md.GetString(f.Name);}else if(h.Kind==HandleKind.MemberReference){var f=md.GetMemberReference((MemberReferenceHandle)h);t=MetadataLoader.ResolveTypeName(md,f.Parent)??throw new NotSupportedException("Field parent.");n=md.GetString(f.Name);}else throw new NotSupportedException($"Field token {h.Kind}.");return m.Fields.TryGetValue((t,n),out var x)?x:throw new NotSupportedException($"Unknown field {t}.{n}.");}
 private static int ReadLocalCount(MetadataReader md,StandaloneSignatureHandle h){var s=md.GetStandaloneSignature(h);var r=md.GetBlobReader(s.Signature);r.ReadSignatureHeader();return r.ReadCompressedInteger();}
 private static int ReadShortBranchTarget(byte[] il,ref int p,int o){Need(il,p,1,o);var delta=(sbyte)il[p++];return p+delta;}
 private static int ReadLongBranchTarget(byte[] il,ref int p,int o){Need(il,p,4,o);var delta=BitConverter.ToInt32(il,p);p+=4;return p+delta;}
 private static EntityHandle ReadToken(byte[] il,ref int p,int o){Need(il,p,4,o);var h=MetadataTokens.EntityHandle(BitConverter.ToInt32(il,p));p+=4;return h;}private static void Need(byte[]il,int p,int n,int o){if(p+n>il.Length)throw new InvalidDataException($"Truncated CIL at IL_{o:x4}.");}private static string Full(string ns,string n)=>string.IsNullOrEmpty(ns)?n:ns+"."+n;
}
