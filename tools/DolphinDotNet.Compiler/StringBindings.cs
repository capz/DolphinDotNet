using System.Text;
namespace DolphinDotNet.Compiler;

internal static class StringBindings
{
    internal static string EmitHelpers(IReadOnlyList<ValueIrMethod> methods,CompilationModel model,DependencyGraph graph)
    {
        if(!methods.SelectMany(m=>m.Blocks).SelectMany(b=>b.Instructions).Any(i=>i is ValueIrObjectText))return "";
        var compiled=methods.Select(m=>m.Key).ToHashSet();var b=new StringBuilder();
        b.AppendLine("static DndString *dnd_generated_to_string(DndObject *a,bool base_call) {if(!dnd_require_object(a))return NULL;if(base_call)return dnd_string_from_utf8(dnd_value_heap,a->type->name);if(a->type==&DND_TYPE_STRING)return (DndString*)a;");
        foreach(var type in new[]{"Int32","UInt32","Int64","UInt64","Byte","SByte","Int16","UInt16","Single","Double","Boolean","Char"})
        {
            int size=type is "Int64" or "UInt64" or "Double"?8:type is "Byte" or "SByte" or "Boolean"?1:type is "Int16" or "UInt16" or "Char"?2:4;
            var tag=ValueCBackend.TypeExpr("System."+type);var raw=$"dnd_unbox_scalar(a,{tag},{size}u)";
            var expression=type switch {
                "Single" or "Double"=>$"dnd_format_float(dnd_value_heap,dnd_float_value({raw},{(type=="Single"?"true":"false")}),{(type=="Single"?1:0)},NULL)",
                "Boolean"=>$"dnd_string_from_bool(dnd_value_heap,{raw}!=0)",
                "Char"=>$"dnd_string_from_char(dnd_value_heap,(uint16_t){raw})",
                "UInt32" or "UInt64" or "Byte" or "UInt16"=>$"dnd_string_from_u64(dnd_value_heap,({(size==8?"uint64_t":size==4?"uint32_t":size==2?"uint16_t":"uint8_t")}){raw})",
                _=>$"dnd_string_from_i64(dnd_value_heap,(int{size*8}_t){raw})"
            };
            b.AppendLine($"if(a->type=={tag})return {expression};");
        }
        foreach(var type in graph.Types.Where(t=>model.Types.ContainsKey(t)&&!model.Types[t].IsInterface))
        {
            string current=type;MethodModel? method=null;
            while(model.Types.TryGetValue(current,out var tm)){
                method=model.Methods.Values.FirstOrDefault(m=>m.Key.TypeName==current&&m.Key.Name=="ToString"&&m.IsVirtual&&!m.IsStatic&&m.ParameterCount==0&&compiled.Contains(m.Key));
                if(method!=null)break;current=tm.BaseType??"";
            }
            if(method!=null)b.AppendLine($"if(a->type=={ValueCBackend.TypeExpr(type)})return (DndString*){ValueCBackend.Symbol(method.Key)}((intptr_t)a{(model.Types[type].IsValueType?"+sizeof(DndObject)":"")});");
        }
        b.AppendLine("return dnd_string_from_utf8(dnd_value_heap,a->type->name);}");return b.ToString();
    }
}
