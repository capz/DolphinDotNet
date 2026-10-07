using System.Reflection.Metadata;

namespace DolphinDotNet.Compiler;

internal static class GenericTypeResolver
{
    public static string DefinitionName(string typeName)
    {
        var bracket=typeName.IndexOf('[');
        return bracket<0?typeName:typeName[..bracket];
    }

    public static string? Resolve(MetadataReader md,CompilationModel model,EntityHandle handle,MethodModel? context=null)
    {
        if(handle.IsNil)return null;
        if(handle.Kind!=HandleKind.TypeSpecification)return MetadataLoader.ResolveTypeName(md,handle);
        var spec=md.GetTypeSpecification((TypeSpecificationHandle)handle);
        var reader=md.GetBlobReader(spec.Signature);
        return ReadType(md,model,ref reader,context);
    }

    private static string? ReadType(MetadataReader md,CompilationModel model,ref BlobReader reader,MethodModel? context)
    {
        var code=reader.ReadSignatureTypeCode();
        return code switch
        {
            SignatureTypeCode.TypeHandle=>Resolve(md,model,reader.ReadTypeHandle(),context),
            SignatureTypeCode.GenericTypeParameter=>ResolveTypeParameter(reader,context),
            SignatureTypeCode.GenericMethodParameter=>SkipMethodParameter(reader),
            SignatureTypeCode.GenericTypeInstance=>ReadGenericInstance(md,model,ref reader,context),
            SignatureTypeCode.SZArray=>Append(ReadType(md,model,ref reader,context),"[]"),
            SignatureTypeCode.Boolean=>"System.Boolean",
            SignatureTypeCode.Byte=>"System.Byte",
            SignatureTypeCode.SByte=>"System.SByte",
            SignatureTypeCode.Char=>"System.Char",
            SignatureTypeCode.Int16=>"System.Int16",
            SignatureTypeCode.UInt16=>"System.UInt16",
            SignatureTypeCode.Int32=>"System.Int32",
            SignatureTypeCode.UInt32=>"System.UInt32",
            SignatureTypeCode.Int64=>"System.Int64",
            SignatureTypeCode.UInt64=>"System.UInt64",
            SignatureTypeCode.Single=>"System.Single",
            SignatureTypeCode.Double=>"System.Double",
            SignatureTypeCode.IntPtr=>"System.IntPtr",
            SignatureTypeCode.UIntPtr=>"System.UIntPtr",
            SignatureTypeCode.String=>"System.String",
            SignatureTypeCode.Object=>"System.Object",
            _=>null
        };
    }

    private static string? ReadGenericInstance(MetadataReader md,CompilationModel model,ref BlobReader reader,MethodModel? context)
    {
        if(reader.ReadSignatureTypeCode()!=SignatureTypeCode.TypeHandle)return null;
        var definition=MetadataLoader.ResolveTypeName(md,reader.ReadTypeHandle());
        if(definition is null)return null;
        var count=reader.ReadCompressedInteger();
        var args=new string[count];
        for(var i=0;i<count;i++)
        {
            var arg=ReadType(md,model,ref reader,context);
            if(arg is null)return definition;
            args[i]=arg;
        }
        return EnsureClosedType(model,definition,args);
    }

    private static string? ResolveTypeParameter(BlobReader reader,MethodModel? context)
    {
        var index=reader.ReadCompressedInteger();
        return context?.TypeArguments is { } args&&index>=0&&index<args.Count?args[index]:null;
    }

    private static string? SkipMethodParameter(BlobReader reader)
    {
        reader.ReadCompressedInteger();
        return null;
    }

    private static string? Append(string? value,string suffix)=>value is null?null:value+suffix;

    public static string EnsureClosedType(CompilationModel model,string definition,IReadOnlyList<string> arguments)
    {
        if(arguments.Count==0)return definition;
        var closed=definition+"["+string.Join(",",arguments)+"]";
        if(model.Types.ContainsKey(closed))return closed;
        if(!model.Types.TryGetValue(definition,out var source))return definition;

        var baseType=CloseRelated(model,source.BaseType,arguments);
        var interfaces=(source.Interfaces??Array.Empty<string>()).Select(x=>CloseRelated(model,x,arguments)??x).ToArray();
        model.Types[closed]=source with { Name=source.Name+"["+string.Join(",",arguments)+"]",FullName=closed,BaseType=baseType,Interfaces=interfaces };

        foreach(var field in model.Fields.Values.Where(x=>x.DeclaringType==definition).ToArray())
            model.Fields[(closed,field.Name)]=field with { DeclaringType=closed };

        foreach(var method in model.Methods.Values.Where(x=>x.Key.TypeName==definition).ToArray())
        {
            var key=method.Key with { TypeName=closed };
            if(!model.Methods.ContainsKey(key))model.Methods[key]=method with { Key=key,TypeArguments=arguments.ToArray() };
        }
        return closed;
    }

    private static string? CloseRelated(CompilationModel model,string? related,IReadOnlyList<string> arguments)
    {
        if(related is null)return null;
        var arity=Arity(related);
        return arity==arguments.Count&&arity>0?EnsureClosedType(model,related,arguments):related;
    }

    private static int Arity(string name)
    {
        var tick=name.LastIndexOf('`');
        if(tick<0)return 0;
        var end=tick+1;
        while(end<name.Length&&char.IsDigit(name[end]))end++;
        return int.TryParse(name[(tick+1)..end],out var value)?value:0;
    }
}