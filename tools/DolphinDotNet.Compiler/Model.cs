using System.Reflection.Metadata;
namespace DolphinDotNet.Compiler;
internal sealed record MethodKey(string TypeName,string Name){public override string ToString()=>$"{TypeName}::{Name}";}
internal sealed record TypeModel(string Namespace,string Name,string FullName,string? BaseType,int InstanceSize);
internal sealed record FieldModel(string DeclaringType,string Name,int Offset,bool IsReference);
internal sealed record MethodModel(MethodKey Key,MethodDefinitionHandle Handle,bool IsStatic,int ParameterCount);
internal sealed class CompilationModel{public required MetadataReader Metadata{get;init;}public Dictionary<string,TypeModel> Types{get;}=new(StringComparer.Ordinal);public Dictionary<MethodKey,MethodModel> Methods{get;}=new();public Dictionary<(string Type,string Field),FieldModel> Fields{get;}=new();}
