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
   var full=DefinitionFullName(md,th);var baseType=ResolveTypeName(md,type.BaseType);var isInterface=(type.Attributes&TypeAttributes.Interface)!=0;
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
    var offset=0;var type=md.GetTypeDefinition(p.Handle);
    foreach(var fh in type.GetFields())
    {
     var field=md.GetFieldDefinition(fh);var isStatic=(field.Attributes&FieldAttributes.Static)!=0;var(size,reference)=FieldLayout(md,field.Signature,model);
     var align=Math.Min(Math.Max(size,1),8);if(!isStatic)offset=Align(offset,align);
     var name=md.GetString(field.Name);var fieldReader=md.GetBlobReader(field.Signature);fieldReader.ReadSignatureHeader();
     var fieldCode=fieldReader.ReadSignatureTypeCode();var aggregateSize=0;IReadOnlyList<int>? embedded=null;
     if(fieldCode==SignatureTypeCode.GenericTypeInstance) { fieldReader.Offset--;var layout=GenericSharing.ReadGenericLocalLayout(md,ref fieldReader,model);aggregateSize=layout.Size;embedded=layout.References; }
     model.Fields[(p.Full,name)]=new(p.Full,name,isStatic?0:offset,reference,isStatic,size,aggregateSize,embedded);if(!isStatic)offset+=size;
    }
    model.Types[p.Full]=new(p.Ns,p.Name,p.Full,p.Base,p.ValueType?Math.Max(offset,type.GetLayout().Size):Align(offset,8),p.Interface,p.ValueType,p.Interfaces);unresolved.Remove(p.Full);progress=true;
    foreach(var mh in type.GetMethods()){var m=md.GetMethodDefinition(mh);var key=new MethodKey(p.Full,md.GetString(m.Name),assemblyName,Convert.ToHexString(md.GetBlobBytes(m.Signature)));var sig=ReadMethodSignature(md,m.Signature);model.Methods[key]=new(key,mh,(m.Attributes&MethodAttributes.Static)!=0,sig.Parameters,sig.ReturnsValue,assemblyName,(m.Attributes&MethodAttributes.Virtual)!=0,(m.Attributes&MethodAttributes.Abstract)!=0,(m.Attributes&MethodAttributes.NewSlot)!=0,p.Interface,p.Base=="System.MulticastDelegate");}
   }
   if(!progress)throw new InvalidDataException("Unable to resolve type layout inheritance.");
  }
  // MethodImpl metadata is authoritative: explicit generic method names use
  // display names such as IEquatable<Foo>, not metadata names with arity.
  foreach(var p in pending)
  foreach(var handle in md.GetTypeDefinition(p.Handle).GetMethodImplementations())
  {
   var implementation=md.GetMethodImplementation(handle);
   if(implementation.MethodBody.Kind!=HandleKind.MethodDefinition)continue;
   string? iface;string name;
   if(implementation.MethodDeclaration.Kind==HandleKind.MemberReference)
   {
    var declaration=md.GetMemberReference((MemberReferenceHandle)implementation.MethodDeclaration);
    iface=ResolveMemberParentTypeName(md,declaration.Parent);name=md.GetString(declaration.Name);
   }
   else if(implementation.MethodDeclaration.Kind==HandleKind.MethodDefinition)
   {
    var declaration=md.GetMethodDefinition((MethodDefinitionHandle)implementation.MethodDeclaration);
    iface=ResolveTypeName(md,declaration.GetDeclaringType());name=md.GetString(declaration.Name);
   }
   else continue;
   if(iface is null)continue;
   var body=model.Methods.Values.Single(m=>m.AssemblyName==assemblyName&&m.Handle==(MethodDefinitionHandle)implementation.MethodBody);
   model.Methods[body.Key]=body with { ExplicitContracts=(body.ExplicitContracts??Array.Empty<(string Interface,string Method)>()).Append((iface,name)).ToArray() };
  }
 }
 internal static void RefreshLayouts(CompilationModel model)
 {
  // Dependencies may be loaded after their callers. Decode fields only after
  // the complete model is available, including embedded generic value types.
  for(var pass=0;pass<model.Types.Count*2+1;pass++){var changed=false;foreach(var assembly in model.Assemblies.Values)
  foreach(var handle in assembly.Metadata.TypeDefinitions)
  {
   var md=assembly.Metadata;var td=md.GetTypeDefinition(handle);if(td.GetGenericParameters().Count>0)continue;
   var name=ResolveTypeName(md,handle);if(name is null||!model.Types.TryGetValue(name,out var tm))continue;
   var offset=0;foreach(var fh in td.GetFields())
   {
    var f=md.GetFieldDefinition(fh);var value=f.DecodeSignature(new SignatureAbi(model),new SignatureAbi.Context(Array.Empty<GenericRepresentation>(),Array.Empty<GenericRepresentation>()));
    var reference=value.Kind==CilStackKind.ObjectReference;var size=value.Size>0?value.Size:reference?8:value.ScalarSize;
    var stat=(f.Attributes&FieldAttributes.Static)!=0;if(!stat)offset=Align(offset,Math.Min(Math.Max(size,1),8));
    var oldField=model.Fields[(name,md.GetString(f.Name))];
    if(oldField.Size!=size||oldField.StorageSize!=value.Size||oldField.IsReference!=reference||!(oldField.ReferenceOffsets??Array.Empty<int>()).SequenceEqual(value.References??Array.Empty<int>()))changed=true;
    model.Fields[(name,md.GetString(f.Name))]=new(name,md.GetString(f.Name),stat?0:offset,reference,stat,size,value.Size,value.References,value.Representation??new GenericRepresentation(reference?GenericRepresentationKind.PointerSized:GenericRepresentationKind.ValueType,size,value.References,value.Name));
    if(!stat)offset+=size;
   }
   var finalSize=tm.IsValueType?Math.Max(offset,td.GetLayout().Size):Align(offset,8);
   if(finalSize!=tm.InstanceSize)changed=true;
   model.Types[name]=tm with {InstanceSize=finalSize};
  }
  foreach(var closed in model.Types.Values.Where(t=>t.GenericDefinition is not null&&t.TypeArguments is not null).ToArray()){
   var definition=model.Methods.Values.FirstOrDefault(m=>m.Key.TypeName==closed.GenericDefinition&&!m.Handle.IsNil);
   if(definition is not null){var before=closed.InstanceSize;SpecializeFields(model,definition,closed.FullName,closed.TypeArguments!);if(before!=model.Types[closed.FullName].InstanceSize)changed=true;}
  }
  if(!changed)break;
  }
 }
 internal static void SpecializeFields(CompilationModel model,MethodModel definition,string typeName,IReadOnlyList<GenericRepresentation> args)
 {
  var md=model.Assemblies[definition.AssemblyName].Metadata;
  var type=md.GetTypeDefinition(md.GetMethodDefinition(definition.Handle).GetDeclaringType());
  var provider=new SignatureAbi(model);var context=new SignatureAbi.Context(args,Array.Empty<GenericRepresentation>());var offset=0;
  foreach(var handle in type.GetFields())
  {
   var field=md.GetFieldDefinition(handle);var name=md.GetString(field.Name);var decoded=field.DecodeSignature(provider,context);
   var isStatic=(field.Attributes&FieldAttributes.Static)!=0;var reference=decoded.Kind==CilStackKind.ObjectReference;
   var size=decoded.Size>0?decoded.Size:reference||decoded.Kind==CilStackKind.I8?8:4;
   var original=model.Fields[(model.Types[typeName].GenericDefinition!,name)];
   if(original.Size<4&&decoded.Size==0&&!reference)size=original.Size;
   if(!isStatic)offset=Align(offset,Math.Min(size,8));
   model.Fields[(typeName,name)]=new(typeName,name,isStatic?0:offset,reference,isStatic,size,decoded.Size,decoded.References,decoded.Representation??new GenericRepresentation(reference?GenericRepresentationKind.PointerSized:GenericRepresentationKind.ValueType,size,decoded.References,decoded.Name));
   if(!isStatic)offset+=size;
  }
  model.Types[typeName]=model.Types[typeName] with { InstanceSize=Align(offset,8) };
 }

 internal static string MapFrameworkType(string name)=>name switch {
  "System.Runtime.CompilerServices.ConfiguredTaskAwaitable"=>"Dolphin.Runtime.CompilerServices.ConfiguredTaskAwaitable",
  "System.Runtime.CompilerServices.ConfiguredTaskAwaitable`1"=>"Dolphin.Runtime.CompilerServices.ConfiguredTaskAwaitable`1",
  "System.Runtime.CompilerServices.ConfiguredTaskAwaitable+ConfiguredTaskAwaiter"=>"Dolphin.Runtime.CompilerServices.ConfiguredTaskAwaitable+ConfiguredTaskAwaiter",
  "System.Runtime.CompilerServices.ConfiguredTaskAwaitable`1+ConfiguredTaskAwaiter"=>"Dolphin.Runtime.CompilerServices.ConfiguredTaskAwaitable`1+ConfiguredTaskAwaiter",

  "System.StringComparer"=>"Dolphin.Collections.StringComparer",
  "System.Threading.CancellationToken"=>"Dolphin.Threading.CancellationToken",
  "System.Threading.CancellationTokenSource"=>"Dolphin.Threading.CancellationTokenSource",
  "System.Threading.Tasks.Task"=>"Dolphin.Threading.Tasks.Task",
  "System.Threading.Tasks.Task`1"=>"Dolphin.Threading.Tasks.Task`1",
  "System.Threading.Tasks.TaskCompletionSource`1"=>"Dolphin.Threading.Tasks.TaskCompletionSource`1",
  "System.Runtime.CompilerServices.TaskAwaiter"=>"Dolphin.Runtime.CompilerServices.TaskAwaiter",
  "System.Runtime.CompilerServices.TaskAwaiter`1"=>"Dolphin.Runtime.CompilerServices.TaskAwaiter`1",
  "System.Runtime.CompilerServices.AsyncTaskMethodBuilder"=>"Dolphin.Runtime.CompilerServices.AsyncTaskMethodBuilder",
  "System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1"=>"Dolphin.Runtime.CompilerServices.AsyncTaskMethodBuilder`1",
  "System.Runtime.CompilerServices.IAsyncStateMachine"=>"Dolphin.Runtime.CompilerServices.IAsyncStateMachine",
  "System.Runtime.CompilerServices.INotifyCompletion"=>"Dolphin.Runtime.CompilerServices.INotifyCompletion",
  "System.Runtime.CompilerServices.ICriticalNotifyCompletion"=>"Dolphin.Runtime.CompilerServices.ICriticalNotifyCompletion",
  "System.Runtime.CompilerServices.YieldAwaitable"=>"Dolphin.Runtime.CompilerServices.YieldAwaitable",
  "System.Runtime.CompilerServices.YieldAwaitable+YieldAwaiter"=>"Dolphin.Runtime.CompilerServices.YieldAwaitable+YieldAwaiter",

  "System.Text.Encoding"=>"Dolphin.Text.Encoding", "System.Text.UTF8Encoding"=>"Dolphin.Text.UTF8Encoding", "System.Text.UnicodeEncoding"=>"Dolphin.Text.UnicodeEncoding",
  "System.IO.TextReader"=>"Dolphin.IO.TextReader", "System.IO.TextWriter"=>"Dolphin.IO.TextWriter", "System.IO.StreamReader"=>"Dolphin.IO.StreamReader", "System.IO.StreamWriter"=>"Dolphin.IO.StreamWriter",
  "System.IO.Stream"=>"Dolphin.IO.Stream", "System.IO.MemoryStream"=>"Dolphin.IO.MemoryStream", "System.IO.FileStream"=>"Dolphin.IO.FileStream",
  "System.Collections.Generic.List`1"=>"Dolphin.Collections.List`1",
  "System.Collections.Generic.List`1+Enumerator"=>"Dolphin.Collections.List`1+Enumerator",
  "System.Collections.Generic.EqualityComparer`1"=>"Dolphin.Collections.EqualityComparer`1",
  "System.Collections.Generic.Comparer`1"=>"Dolphin.Collections.Comparer`1",
  _=>name };
 public static string? ResolveTypeName(MetadataReader md,EntityHandle h)
 {
  if(h.IsNil)return null;
  if(h.Kind==HandleKind.TypeDefinition){var t=md.GetTypeDefinition((TypeDefinitionHandle)h);return DefinitionFullName(md,(TypeDefinitionHandle)h);}
  if(h.Kind==HandleKind.TypeReference){var t=md.GetTypeReference((TypeReferenceHandle)h);return MapFrameworkType(ReferenceFullName(md,(TypeReferenceHandle)h));}
  if(h.Kind==HandleKind.TypeSpecification)return ResolveTypeSpecificationName(md,(TypeSpecificationHandle)h);
  return null;
 }
 public static string? ResolveMemberParentTypeName(MetadataReader md,EntityHandle h)
 {
  if(h.Kind!=HandleKind.TypeSpecification)return ResolveTypeName(md,h);
  return ResolveTypeSpecificationName(md,(TypeSpecificationHandle)h);
 }
 private static string? ResolveTypeSpecificationName(MetadataReader md,TypeSpecificationHandle h)
 {
  // Phase 4 generic sharing: a closed generic type uses the metadata/layout and
  // code of its generic definition.  We intentionally do not materialize a
  // per-instantiation TypeModel here; that keeps AOT metadata/code growth flat.
  var spec=md.GetTypeSpecification(h);var r=md.GetBlobReader(spec.Signature);
  var code=r.ReadSignatureTypeCode();
  if(code!=SignatureTypeCode.GenericTypeInstance)return null;
  // ECMA-335 encodes the generic type after GENERICINST as CLASS (0x12) or
  // VALUETYPE (0x11), followed by a TypeDefOrRef coded index. BlobReader's
  // ReadSignatureTypeCode maps those bytes to TypeHandle, so the handle itself
  // is the generic definition that all closed instantiations share.
  if(r.ReadSignatureTypeCode()!=SignatureTypeCode.TypeHandle)return null;
  return ResolveTypeName(md,r.ReadTypeHandle());
 }
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
   SignatureTypeCode.String or SignatureTypeCode.Object or SignatureTypeCode.SZArray or SignatureTypeCode.Array=>(8,true),
   SignatureTypeCode.TypeHandle=>ReadTypeHandleLayout(md,ref r,model),
   SignatureTypeCode.GenericTypeInstance=>ReadGenericFieldLayout(md,ref r,model),
   _=>(8,false)
  };
 }
 private static (int Size,bool Reference) ReadGenericFieldLayout(MetadataReader md,ref BlobReader reader,CompilationModel model)
 { reader.Offset--;var layout=GenericSharing.ReadGenericLocalLayout(md,ref reader,model);return layout.Size>0?(layout.Size,false):(8,true); }
 private static (int Size,bool Reference) ReadTypeHandleLayout(MetadataReader md,ref BlobReader r,CompilationModel model){var h=r.ReadTypeHandle();var name=ResolveTypeName(md,h);return name is "System.IO.FileMode" or "System.IO.FileAccess" or "System.IO.FileShare" or "System.IO.SeekOrigin" or "System.IO.SearchOption"?(4,false):name is not null&&model.Types.TryGetValue(name,out var t)&&t.IsValueType?(Math.Max(1,t.InstanceSize),false):(8,true);}
 private static int Align(int value,int alignment)=>(value+alignment-1)&~(alignment-1);
 private static string DefinitionFullName(MetadataReader md,TypeDefinitionHandle handle){var t=md.GetTypeDefinition(handle);var name=md.GetString(t.Name);var declaring=t.GetDeclaringType();return declaring.IsNil?Full(md.GetString(t.Namespace),name):DefinitionFullName(md,declaring)+"+"+name;}
 private static string ReferenceFullName(MetadataReader md,TypeReferenceHandle handle){var t=md.GetTypeReference(handle);var name=md.GetString(t.Name);return t.ResolutionScope.Kind==HandleKind.TypeReference?ReferenceFullName(md,(TypeReferenceHandle)t.ResolutionScope)+"+"+name:Full(md.GetString(t.Namespace),name);}
 private static string Full(string ns,string name)=>string.IsNullOrEmpty(ns)?name:ns+"."+name;
}
