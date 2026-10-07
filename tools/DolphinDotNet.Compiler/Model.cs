using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
namespace DolphinDotNet.Compiler;
internal sealed record MethodKey(string TypeName,string Name,string AssemblyName="",string Signature=""){public override string ToString()=>$"{(string.IsNullOrEmpty(AssemblyName)?"":AssemblyName+"!")} {TypeName}::{Name}{(string.IsNullOrEmpty(Signature)?"":" ["+Signature+"]")}";}
internal sealed record TypeModel(string Namespace,string Name,string FullName,string? BaseType,int InstanceSize,bool IsInterface=false,bool IsValueType=false,IReadOnlyList<string>? Interfaces=null);
internal sealed record FieldModel(string DeclaringType,string Name,int Offset,bool IsReference,bool IsStatic=false,int Size=4);
internal readonly record struct LocalStorage(int Size,CilStackKind Kind);
internal sealed record GenericAbi(IReadOnlyList<CilStackKind> Parameters,CilStackKind? Return);
internal sealed record MethodModel(MethodKey Key,MethodDefinitionHandle Handle,bool IsStatic,int ParameterCount,bool ReturnsValue,string AssemblyName,bool IsVirtual=false,bool IsAbstract=false,bool IsNewSlot=false,bool DeclaringTypeIsInterface=false,bool DeclaringTypeIsDelegate=false,GenericAbi? Abi=null);
internal sealed class AssemblyModel : IDisposable
{
 public required string Name{get;init;} public required string Path{get;init;} public required FileStream Stream{get;init;} public required PEReader PE{get;init;} public required MetadataReader Metadata{get;init;}
 public void Dispose(){PE.Dispose();Stream.Dispose();}
}
internal sealed class CompilationModel : IDisposable
{
 public Dictionary<string,AssemblyModel> Assemblies{get;}=new(StringComparer.OrdinalIgnoreCase);
 public Dictionary<string,TypeModel> Types{get;}=new(StringComparer.Ordinal);
 public Dictionary<MethodKey,MethodModel> Methods{get;}=new();
 public Dictionary<(string Type,string Field),FieldModel> Fields{get;}=new();
 public void Dispose(){foreach(var a in Assemblies.Values)a.Dispose();}
}
