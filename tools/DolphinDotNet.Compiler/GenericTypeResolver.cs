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
        if(!model.Types.TryGetValue(definition,out var source))return closed;

        var baseType=CloseRelated(model,source.BaseType,arguments);
        var interfaces=(source.Interfaces??Array.Empty<string>()).Select(x=>CloseRelated(model,x,arguments)??x).ToArray();

        var ownSize=0;
        foreach(var field in model.Fields.Values.Where(x=>x.DeclaringType==definition).OrderBy(x=>x.IsStatic?1:0).ThenBy(x=>x.Offset).ToArray())
        {
            var size=field.Size;var reference=field.IsReference;var embedded=field.EmbeddedReferenceOffsets;
            if(field.GenericParameterIndex>=0&&field.GenericParameterIndex<arguments.Count)
                (size,reference,embedded)=ArgumentLayout(model,arguments[field.GenericParameterIndex]);
            var offset=field.IsStatic?0:Align(ownSize,Math.Min(Math.Max(size,1),4));
            model.Fields[(closed,field.Name)]=field with { DeclaringType=closed,Offset=offset,Size=size,IsReference=reference,EmbeddedReferenceOffsets=embedded };
            if(!field.IsStatic)ownSize=offset+size;
        }

        model.Types[closed]=source with { Name=source.Name+"["+string.Join(",",arguments)+"]",FullName=closed,BaseType=baseType,Interfaces=interfaces,InstanceSize=ownSize };

        foreach(var method in model.Methods.Values.Where(x=>x.Key.TypeName==definition).ToArray())
        {
            var key=method.Key with { TypeName=closed };
            if(!model.Methods.ContainsKey(key))model.Methods[key]=method with { Key=key,TypeArguments=arguments.ToArray() };
        }
        return closed;
    }

    private static (int Size,bool Reference,IReadOnlyList<int> Embedded) ArgumentLayout(CompilationModel model,string type)
    {
        switch(type)
        {
            case "System.Boolean":case "System.Byte":case "System.SByte":return(1,false,Array.Empty<int>());
            case "System.Char":case "System.Int16":case "System.UInt16":return(2,false,Array.Empty<int>());
            case "System.Int64":case "System.UInt64":case "System.Double":return(8,false,Array.Empty<int>());
            case "System.Single":case "System.Int32":case "System.UInt32":case "System.IntPtr":case "System.UIntPtr":return(4,false,Array.Empty<int>());
            case "System.String":case "System.Object":return(4,true,new[]{0});
        }
        if(type.EndsWith("[]",StringComparison.Ordinal))return(4,true,new[]{0});
        if(model.Types.TryGetValue(type,out var tm))
        {
            if(!tm.IsValueType)return(4,true,new[]{0});
            var refs=model.Fields.Values.Where(f=>f.DeclaringType==type&&!f.IsStatic)
                .SelectMany(f=>(f.EmbeddedReferenceOffsets??(f.IsReference?new[]{0}:Array.Empty<int>())).Select(o=>f.Offset+o)).Distinct().OrderBy(x=>x).ToArray();
            return(Math.Max(1,tm.InstanceSize),false,refs);
        }
        return(4,true,new[]{0});
    }

    private static int Align(int value,int alignment)=>(value+alignment-1)&~(alignment-1);

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