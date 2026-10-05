using System.Text;
namespace DolphinDotNet.Compiler;

internal static class ValueCBackend
{
    public static string Emit(ValueIrMethod method,string functionName="dnd_value_ir_test")
    {
        var b=new StringBuilder();
        var values=Collect(method).OrderBy(v=>v.Id).ToArray();
        b.AppendLine("#include <stdint.h>");
        b.Append($"intptr_t {functionName}(");
        for(var i=0;i<method.ParameterCount+(method.HasThis?1:0);i++){if(i>0)b.Append(", ");b.Append($"intptr_t a{i}");}
        b.AppendLine(") {");
        foreach(var v in values)b.AppendLine($"  intptr_t v{v.Id} = 0;");
        foreach(var local in method.Locals)b.AppendLine($"  {CType(local.Kind)} l{local.Index} = 0;");
        if(method.Blocks.Count>0)b.AppendLine($"  goto block_{method.Blocks[0].Id};");
        foreach(var block in method.Blocks)
        {
            b.AppendLine($"block_{block.Id}:");
            foreach(var i in block.Instructions)
            {
                switch(i)
                {
                    case ValueIrConstant x:b.AppendLine($"  v{x.Result.Id} = {x.Value};");break;
                    case ValueIrLoadArgument x:b.AppendLine($"  v{x.Result.Id} = a{x.Index};");break;
                    case ValueIrLoadLocal x:b.AppendLine($"  v{x.Result.Id} = l{x.Index};");break;
                    case ValueIrStoreLocal x:b.AppendLine($"  l{x.Index} = v{x.Value.Id};");break;
                    case ValueIrBinary x:b.AppendLine($"  v{x.Result.Id} = v{x.Left.Id} {Op(x.Operation)} v{x.Right.Id};");break;
                    case ValueIrPhi: break; // Assigned on predecessor edges.
                }
            }
            EmitTerminator(b,method,block);
        }
        b.AppendLine("}");
        return b.ToString();
    }

    private static void EmitTerminator(StringBuilder b,ValueIrMethod method,ValueIrBlock block)
    {
        switch(block.Terminator)
        {
            case ValueIrJump j: Edge(b,method,block.Id,j.TargetBlock);b.AppendLine($"  goto block_{j.TargetBlock};");break;
            case ValueIrBranch x:
                var left=x.Unsigned?$"(uintptr_t)v{x.Left.Id}":$"v{x.Left.Id}";
                var right=x.Right is { } rv?(x.Unsigned?$"(uintptr_t)v{rv.Id}":$"v{rv.Id}"):"0";
                var op=x.Comparison switch{ValueIrComparison.NonZero=>"!=",ValueIrComparison.Equal=>"==",ValueIrComparison.NotEqual=>"!=",ValueIrComparison.GreaterThan=>">",ValueIrComparison.GreaterOrEqual=>">=",ValueIrComparison.LessThan=>"<",ValueIrComparison.LessOrEqual=>"<=",_=>throw new InvalidDataException()};
                b.AppendLine($"  if ({left} {op} {right}) {{");Edge(b,method,block.Id,x.TrueBlock,"    ");b.AppendLine($"    goto block_{x.TrueBlock};");b.AppendLine("  } else {");Edge(b,method,block.Id,x.FalseBlock,"    ");b.AppendLine($"    goto block_{x.FalseBlock};");b.AppendLine("  }");break;
            case ValueIrReturn r:b.AppendLine(r.Value is { } v?$"  return v{v.Id};":"  return 0;");break;
            case null:b.AppendLine("  return 0;");break;
        }
    }
    private static void Edge(StringBuilder b,ValueIrMethod method,int from,int to,string indent="  ")
    {
        var target=method.Blocks.Single(x=>x.Id==to);
        foreach(var phi in target.Instructions.OfType<ValueIrPhi>())
            if(phi.Inputs.TryGetValue(from,out var input))b.AppendLine($"{indent}v{phi.Result.Id} = v{input.Id};");
    }
    private static string CType(IrValueKind kind)=>kind switch
    {
        IrValueKind.R4=>"float",
        IrValueKind.R8=>"double",
        IrValueKind.I8=>"int64_t",
        IrValueKind.ObjectReference or IrValueKind.ManagedPointer or IrValueKind.NativeInt=>"intptr_t",
        _=>"int32_t"
    };
    private static string Op(string op)=>op switch{"add"=>"+","sub"=>"-","mul"=>"*",_=>throw new InvalidDataException($"Unsupported Value IR binary operation {op}.")};
    private static IEnumerable<IrValue> Collect(ValueIrMethod m)
    {
        foreach(var b in m.Blocks)foreach(var i in b.Instructions)switch(i)
        {
            case ValueIrConstant x:yield return x.Result;break;case ValueIrLoadArgument x:yield return x.Result;break;case ValueIrLoadLocal x:yield return x.Result;break;case ValueIrBinary x:yield return x.Result;break;case ValueIrPhi x:yield return x.Result;break;
        }
    }
}
