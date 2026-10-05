using System.Text;
namespace DolphinDotNet.Compiler;

internal static class ValueCBackend
{
    public static string EmitProgram(IReadOnlyList<ValueIrMethod> methods,MethodKey entry,CompilationModel model,DependencyGraph graph)
    {
        var b=new StringBuilder();b.AppendLine("#include <stdint.h>\n#include \"dnd_managed.h\"\n#include \"dnd_console.h\"\n#include \"dnd_input.h\"\nstatic DndManagedHeap *dnd_value_heap;");foreach(var tn in graph.Types.OrderBy(x=>x)){if(!model.Types.TryGetValue(tn,out var t))continue;var parent=t.BaseType!=null&&graph.Types.Contains(t.BaseType)&&model.Types.ContainsKey(t.BaseType)?$"&dnd_type_{Id(t.BaseType)}":"&DND_TYPE_OBJECT";b.AppendLine($"const DndType dnd_type_{Id(tn)} = {{\"{tn}\", {parent}, sizeof(DndObject)+{t.InstanceSize}u, 0, NULL}};");}
        foreach(var m in methods)b.AppendLine($"intptr_t {Symbol(m.Key)}({Parameters(m)});");
        foreach(var m in methods)b.AppendLine(Emit(m,model,Symbol(m.Key),false));
        var em=methods.Single(m=>m.Key==entry);b.Append($"intptr_t dnd_value_aot_entry(DndManagedHeap *heap) {{ dnd_value_heap=heap; return {Symbol(entry)}(");b.Append(string.Join(", ",Enumerable.Range(0,em.ParameterCount+(em.HasThis?1:0)).Select(i=>$"a{i}")));b.AppendLine("); }");return b.ToString();
    }

    public static string Emit(ValueIrMethod method,CompilationModel model,string functionName="dnd_value_ir_test",bool includeHeader=true)
    {
        var b=new StringBuilder();
        var values=Collect(method).GroupBy(v=>v.Id).Select(g=>g.First()).OrderBy(v=>v.Id).ToArray();
        if(includeHeader)b.AppendLine("#include <stdint.h>");
        b.Append($"intptr_t {functionName}(");
        for(var i=0;i<method.ParameterCount+(method.HasThis?1:0);i++){if(i>0)b.Append(", ");b.Append($"intptr_t a{i}");}
        b.AppendLine(") {");
        foreach(var v in values)b.AppendLine($"  intptr_t v{v.Id} = 0; (void)v{v.Id};");
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
                    case ValueIrLoadString x:b.AppendLine($"  v{x.Result.Id} = (intptr_t)dnd_string_from_utf8(dnd_value_heap, \"{Escape(x.Value)}\");");break;
                    case ValueIrLoadArgument x:b.AppendLine($"  v{x.Result.Id} = a{x.Index};");break;
                    case ValueIrLoadLocal x:b.AppendLine($"  v{x.Result.Id} = l{x.Index};");break;
                    case ValueIrStoreLocal x:b.AppendLine($"  l{x.Index} = v{x.Value.Id};");break;
                    case ValueIrStoreArgument x:b.AppendLine($"  a{x.Index} = v{x.Value.Id};");break;
                    case ValueIrBinary x:{var unsigned=x.Operation.EndsWith(".un",StringComparison.Ordinal);var op=Op(x.Operation);var l=unsigned?$"(uintptr_t)v{x.Left.Id}":$"v{x.Left.Id}";var r=unsigned?$"(uintptr_t)v{x.Right.Id}":$"v{x.Right.Id}";b.AppendLine($"  v{x.Result.Id} = {l} {op} {r};");break;}
                    case ValueIrCall x:{var args=string.Join(", ",x.Arguments.Select(a=>$"v{a.Id}"));b.AppendLine(x.Result is { } r?$"  v{r.Id} = {Symbol(x.Target)}({args});":$"  (void){Symbol(x.Target)}({args});");break;}
                    case ValueIrNewObject x:{var args=string.Join(", ",new[]{$"(intptr_t)v{x.Result.Id}"}.Concat(x.Arguments.Select(a=>$"v{a.Id}")));b.AppendLine($"  v{x.Result.Id} = (intptr_t)dnd_object_new(dnd_value_heap, &dnd_type_{Id(x.TypeName)});");b.AppendLine($"  (void){Symbol(x.Constructor)}({args});");break;}
                    case ValueIrLoadField x:{var field=model.Fields[(x.TypeName,x.FieldName)];b.AppendLine($"  v{x.Result.Id} = *(int32_t*)((uint8_t*)v{x.Object.Id}+sizeof(DndObject)+{field.Offset});");break;}
                    case ValueIrStoreField x:{var field=model.Fields[(x.TypeName,x.FieldName)];b.AppendLine($"  *(int32_t*)((uint8_t*)v{x.Object.Id}+sizeof(DndObject)+{field.Offset}) = (int32_t)v{x.Value.Id};");break;}
                    case ValueIrStringLength x:b.AppendLine($"  v{x.Result.Id} = ((DndString*)v{x.String.Id})->length;");break;
                    case ValueIrConsoleWriteLine x:b.AppendLine($"  {{ DndString *s=(DndString*)v{x.String.Id}; char text[256]; size_t n=s&&s->length<255?s->length:255; for(size_t i=0;i<n;i++) text[i]=(char)(s->chars[i]&0x7f); text[n]=0; dnd_console_write_line(text); }}");break;
                    case ValueIrReadButtonsDown x:b.AppendLine($"  dnd_input_poll(); {{ const DndGamePad *pad=dnd_input_gamepad((unsigned)v{x.Port.Id}); v{x.Result.Id}=pad?(intptr_t)pad->down:0; }}");break;
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
            case ValueIrSwitch x:
                b.AppendLine($"  switch ((int32_t)v{x.Value.Id}) {{");for(var i=0;i<x.Targets.Count;i++){b.AppendLine($"    case {i}:");Edge(b,method,block.Id,x.Targets[i],"      ");b.AppendLine($"      goto block_{x.Targets[i]};");}b.AppendLine("    default:");Edge(b,method,block.Id,x.DefaultBlock,"      ");b.AppendLine($"      goto block_{x.DefaultBlock};");b.AppendLine("  }");break;
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
    internal static string Symbol(MethodKey k)=>"dnd_value_"+Id(k.TypeName)+"_"+Id(k.Name);
    private static string Parameters(ValueIrMethod m){var n=m.ParameterCount+(m.HasThis?1:0);return n==0?"void":string.Join(", ",Enumerable.Range(0,n).Select(i=>$"intptr_t a{i}"));}
    private static string Id(string s)=>new(s.Select(ch=>char.IsLetterOrDigit(ch)?ch:'_').ToArray());
    private static string Escape(string s)=>s.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\n","\\n").Replace("\r","\\r").Replace("\t","\\t");
    private static string CType(IrValueKind kind)=>kind switch
    {
        IrValueKind.R4=>"float",
        IrValueKind.R8=>"double",
        IrValueKind.I8=>"int64_t",
        IrValueKind.ObjectReference or IrValueKind.ManagedPointer or IrValueKind.NativeInt=>"intptr_t",
        _=>"int32_t"
    };
    private static string Op(string op)=>op switch{"add"=>"+","sub"=>"-","mul"=>"*","ceq"=>"==","cgt" or "cgt.un"=>">","clt" or "clt.un"=>"<",_=>throw new InvalidDataException($"Unsupported Value IR binary operation {op}.")};
    private static IEnumerable<IrValue> Collect(ValueIrMethod m)
    {
        foreach(var b in m.Blocks)foreach(var i in b.Instructions)switch(i)
        {
            case ValueIrCall x:if(x.Result is { } cr)yield return cr;foreach(var a in x.Arguments)yield return a;break;case ValueIrNewObject x:yield return x.Result;foreach(var a in x.Arguments)yield return a;break;case ValueIrLoadField x:yield return x.Result;yield return x.Object;break;case ValueIrStoreField x:yield return x.Object;yield return x.Value;break;case ValueIrLoadString x:yield return x.Result;break;case ValueIrStringLength x:yield return x.Result;yield return x.String;break;case ValueIrConsoleWriteLine x:yield return x.String;break;case ValueIrReadButtonsDown x:yield return x.Result;yield return x.Port;break;case ValueIrConstant x:yield return x.Result;break;case ValueIrLoadArgument x:yield return x.Result;break;case ValueIrLoadLocal x:yield return x.Result;break;case ValueIrStoreLocal x:yield return x.Value;break;case ValueIrStoreArgument x:yield return x.Value;break;case ValueIrBinary x:yield return x.Result;break;case ValueIrPhi x:yield return x.Result;foreach(var v in x.Inputs.Values)yield return v;break;case ValueIrOpaqueStackEffect x:foreach(var v in x.Results)yield return v;break;
        }
        foreach(var b in m.Blocks){foreach(var v in b.EntryStack.Values)yield return v;foreach(var v in b.ExitStack.Values)yield return v;if(b.Terminator is ValueIrBranch br){yield return br.Left;if(br.Right is { } r)yield return r;}else if(b.Terminator is ValueIrSwitch sw)yield return sw.Value;else if(b.Terminator is ValueIrReturn ret&&ret.Value is { } rv)yield return rv;}
    }
}
