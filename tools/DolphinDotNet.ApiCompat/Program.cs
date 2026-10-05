using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

if(args.Length!=2){Console.Error.WriteLine("Usage: dnd-api-compat <reference-contract.dll> <implementation.dll>");return 2;}
var reference=Read(args[0]);var implementation=Read(args[1]);
var missing=reference.Except(implementation,StringComparer.Ordinal).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
foreach(var item in missing)Console.WriteLine(item);
Console.Error.WriteLine($"Reference APIs: {reference.Count}; implemented: {reference.Count-missing.Length}; missing: {missing.Length}; coverage: {(reference.Count==0?100.0:100.0*(reference.Count-missing.Length)/reference.Count):F2}%");
return missing.Length==0?0:1;

static HashSet<string> Read(string path)
{
    using var stream=File.OpenRead(path);using var pe=new PEReader(stream);var md=pe.GetMetadataReader();var result=new HashSet<string>(StringComparer.Ordinal);
    foreach(var handle in md.TypeDefinitions)
    {
        var type=md.GetTypeDefinition(handle);if(!IsPublic(type.Attributes))continue;
        var typeName=Full(md.GetString(type.Namespace),md.GetString(type.Name));result.Add("T:"+typeName);
        foreach(var mh in type.GetMethods()){var method=md.GetMethodDefinition(mh);if((method.Attributes&MethodAttributes.MemberAccessMask)!=MethodAttributes.Public)continue;result.Add($"M:{typeName}::{md.GetString(method.Name)}:{Convert.ToHexString(md.GetBlobBytes(method.Signature))}");}
        foreach(var fh in type.GetFields()){var field=md.GetFieldDefinition(fh);if((field.Attributes&FieldAttributes.FieldAccessMask)!=FieldAttributes.Public)continue;result.Add($"F:{typeName}::{md.GetString(field.Name)}:{Convert.ToHexString(md.GetBlobBytes(field.Signature))}");}
        foreach(var ph in type.GetProperties()){var property=md.GetPropertyDefinition(ph);result.Add($"P:{typeName}::{md.GetString(property.Name)}:{Convert.ToHexString(md.GetBlobBytes(property.Signature))}");}
        foreach(var eh in type.GetEvents()){var ev=md.GetEventDefinition(eh);result.Add($"E:{typeName}::{md.GetString(ev.Name)}");}
    }
    return result;
}
static bool IsPublic(TypeAttributes a)=>(a&TypeAttributes.VisibilityMask) is TypeAttributes.Public or TypeAttributes.NestedPublic;
static string Full(string ns,string name)=>string.IsNullOrEmpty(ns)?name:ns+"."+name;
