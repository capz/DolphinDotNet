using System.Reflection;
using System.Reflection.Metadata;
namespace DolphinDotNet.Compiler;
internal static class MetadataLoader
{
 public static void LoadInto(CompilationModel model,MetadataReader md,string assemblyName)
 {
  var pending=new List<(TypeDefinitionHandle Handle,string Ns,string Name,string Full,string? Base,bool Interface,bool ValueType,string[] Interfaces)>();
  foreach(var th in md.TypeDefinitions)
  {
   var type=md.GetTypeDefinition(th);var ns=md.GetString(type.Namespace);var name=md.GetString(type.Name);if(name=="<Module>")continue;
   var full=Full(ns,name);var baseType=ResolveTypeName(md,type.BaseType);var isInterface=(type.Attributes&TypeAttributes.Interface)!=0;
   var interfaces=type.GetInterfaceImplementations().Select(h=>ResolveTypeName(md,md.GetInterfaceImplementation(h).Interface)).Where(x=>x is not null).Cast<string>().ToArray();
   pending.Add((th,ns,name,full,baseType,isInterface,baseType=="System.ValueType"||baseType=="System.Enum",interfaces));
  }
  var unresolved=new HashSet<string>(pending.Select(x=>x.Full));
  while(unresolved.Count>0)
  {
   var progress=false;
   foreach(var p in pending.Where(x=>unresolved.Contains(x.Full)).ToArray())
   {
    if(p.Base is { } b&&unresolved.Contains(b))continue;
    var dependencies=ValueTypeFieldDependencies(md,md.GetTypeDefinition(p.Handle)).Where(unresolved.Contains).ToArray();
    if(dependencies.Length>0)continue;
    var offset=0;var type=md.GetTypeDefinition(p.Handle);
    foreach(var fh in type.GetFields())
    {
     var field=md.GetFieldDefinition(fh);var isStatic=(field.Attributes&FieldAttributes.Static)!=0;var(size,reference)=FieldLayout(md,field.Signature,model);
     var align=Math.Min(Math.Max(size,1),4);if(!isStatic)offset=Align(offset,align);
     var name=md.GetString(field.Name);var embedded=EmbeddedReferences(md,field.Signature,model);model.Fields[(p.Full,name)]=new(p.Full,name,isStatic?0:offset,reference,isStatic,size,embedded);if(!isStatic)offset+=size;
    }
    model.Types[p.Full]=new(p.Ns,p.Name,p.Full,p.Base,offset,p.Interface,p.ValueType,p.Interfaces);unresolved.Remove(p.Full);progress=true;
    foreach(var mh in type.GetMethods()){var m=md.GetMethodDefinition(mh);var key=new MethodKey(p.Full,md.GetString(m.Name),assemblyName,Convert.ToHexString(md.GetBlobBytes(m.Signature)));var sig=ReadMethodSignature(md,m.Signature);model.Methods[key]=new(key,mh,(m.Attributes&MethodAttributes.Static)!=0,sig.Parameters,sig.ReturnsValue,assemblyName,(m.Attributes&MethodAttributes.Virtual)!=0,(m.Attributes&MethodAttributes.Abstract)!=0,(m.Attributes&MethodAttributes.NewSlot)!=0,p.Interface,p.Base=="System.MulticastDelegate");}
   }
   if(!progress)throw new InvalidDataException("Unable to resolve type layout inheritance.");
  }
 }
 public static string? ResolveTypeName(MetadataReader md,EntityHandle h){if(h.IsNil)return null;if(h.Kind==HandleKind.TypeDefinition){var t=md.GetTypeDefinition((TypeDefinitionHandle)h);return Full(md.GetString(t.Namespace),md.GetString(t.Name));}if(h.Kind==HandleKind.TypeReference){var t=md.GetTypeReference((TypeReferenceHandle)h);return Full(md.GetString(t.Namespace),md.GetString(t.Name));}if(h.Kind==HandleKind.TypeSpecification)return null;return null;}
 private static (int Parameters,bool ReturnsValue) ReadMethodSignature(MetadataReader md,BlobHandle sig){var r=md.GetBlobReader(sig);var h=r.ReadSignatureHeader();if(h.IsGeneric)r.ReadCompressedInteger();int p=r.ReadCompressedInteger();var ret=r.ReadSignatureTypeCode();return(p,ret!=SignatureTypeCode.Void);}
 private static(int Size,bool Reference)FieldLayout(MetadataReader md,BlobHandle sig,CompilationModel model){var r=md.GetBlobReader(sig);r.ReadSignatureHeader();return ReadFieldType(md,ref r,model);}
 private static(int Size,bool Reference)ReadFieldType(MetadataReader md,ref BlobReader r,CompilationModel model)
 {
  var c=r.ReadSignatureTypeCode();
  return c switch
  {
   SignatureTypeCode.Boolean or SignatureTypeCode.Byte or SignatureTypeCode.SByte=>(1,false),
   SignatureTypeCode.Char or SignatureTypeCode.Int16 or SignatureTypeCode.UInt16=>(2,false),
   SignatureTypeCode.Int64 or SignatureTypeCode.UInt64 or SignatureTypeCode.Double=>(8,false),
   SignatureTypeCode.Single or SignatureTypeCode.Int32 or SignatureTypeCode.UInt32=>(4,false),
   SignatureTypeCode.IntPtr or SignatureTypeCode.UIntPtr=>(4,false),
   SignatureTypeCode.String or SignatureTypeCode.Object or SignatureTypeCode.SZArray or SignatureTypeCode.Array=>(4,true),
   SignatureTypeCode.TypeHandle=>ReadTypeHandleLayout(md,ref r,model),
   _=>(4,false)
  };
 }
 private static (int Size,bool Reference) ReadTypeHandleLayout(MetadataReader md,ref BlobReader r,CompilationModel model){var h=r.ReadTypeHandle();var name=ResolveTypeName(md,h);return name is not null&&model.Types.TryGetValue(name,out var t)&&t.IsValueType?(Math.Max(1,t.InstanceSize),false):(4,true);}
 private static IReadOnlyList<int> EmbeddedReferences(MetadataReader md,BlobHandle sig,CompilationModel model)
 {
  var r=md.GetBlobReader(sig);r.ReadSignatureHeader();var code=r.ReadSignatureTypeCode();
  if(code is SignatureTypeCode.String or SignatureTypeCode.Object or SignatureTypeCode.SZArray or SignatureTypeCode.Array)return new[]{0};
  if(code!=SignatureTypeCode.TypeHandle)return Array.Empty<int>();
  var name=ResolveTypeName(md,r.ReadTypeHandle());if(name is null||!model.Types.TryGetValue(name,out var t)||!t.IsValueType)return Array.Empty<int>();
  return model.Fields.Values.Where(f=>f.DeclaringType==name&&!f.IsStatic).SelectMany(f=>(f.EmbeddedReferenceOffsets??(f.IsReference?new[]{0}:Array.Empty<int>())).Select(o=>f.Offset+o)).ToArray();
 }
 private static IEnumerable<string> ValueTypeFieldDependencies(MetadataReader md,TypeDefinition type)
 {
  foreach(var fh in type.GetFields())
  {
   var field=md.GetFieldDefinition(fh);if((field.Attributes&FieldAttributes.Static)!=0)continue;
   var r=md.GetBlobReader(field.Signature);r.ReadSignatureHeader();if(r.ReadSignatureTypeCode()!=SignatureTypeCode.TypeHandle)continue;
   var name=ResolveTypeName(md,r.ReadTypeHandle());if(name is not null)yield return name;
  }
 }
 private static int Align(int value,int alignment)=>(value+alignment-1)&~(alignment-1);
 private static string Full(string ns,string name)=>string.IsNullOrEmpty(ns)?name:ns+"."+name;
}
