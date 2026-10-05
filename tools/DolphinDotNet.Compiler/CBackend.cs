using System.Text;
namespace DolphinDotNet.Compiler;
internal static class CBackend{
 public static string Emit(IEnumerable<IrMethod> source,CompilationModel model,DependencyGraph graph){
  var methods=source.ToList();var b=new StringBuilder();b.AppendLine("/* DolphinDotNet closed-world AOT output. */\n#include <stdint.h>\n#include <string.h>\n#include \"dnd_managed.h\"");
  foreach(var tn in graph.Types.OrderBy(x=>x)){if(!model.Types.TryGetValue(tn,out var t))continue;var parent=t.BaseType!=null&&graph.Types.Contains(t.BaseType)&&model.Types.ContainsKey(t.BaseType)?$"&dnd_type_{Id(t.BaseType)}":"&DND_TYPE_OBJECT";b.AppendLine($"static const DndType dnd_type_{Id(tn)} = {{\"{tn}\", {parent}, sizeof(DndObject)+{t.InstanceSize}u, 0, NULL}};");}
  foreach(var m in methods)b.AppendLine($"static intptr_t dnd_method_{Id(m.Key.TypeName)}_{Id(m.Key.Name)}(DndManagedHeap*, intptr_t*);");
  foreach(var m in methods)EmitMethod(b,m,model);
  return b.ToString();
 }
 private static void EmitMethod(StringBuilder b,IrMethod m,CompilationModel model){
  string fn=$"dnd_method_{Id(m.Key.TypeName)}_{Id(m.Key.Name)}";b.AppendLine($"static intptr_t {fn}(DndManagedHeap *heap, intptr_t *args) {{");b.AppendLine("    intptr_t stack[64]; int sp=0;");b.AppendLine($"    intptr_t locals[{Math.Max(1,m.LocalCount)}]; memset(locals,0,sizeof(locals));");
  foreach(var i in m.Instructions)Emit(b,i,model);b.AppendLine("    return 0;\n}");
 }
 private static void Emit(StringBuilder b,IrInstruction i,CompilationModel model){
  switch(i){
   case IrConstI4 x:b.AppendLine($"    stack[sp++] = (intptr_t){x.Value};");break;
   case IrLoadArg x:b.AppendLine($"    stack[sp++] = args[{x.Index}];");break;
   case IrLoadLocal x:b.AppendLine($"    stack[sp++] = locals[{x.Index}];");break;
   case IrStoreLocal x:b.AppendLine($"    locals[{x.Index}] = stack[--sp];");break;
   case IrDup:b.AppendLine("    stack[sp] = stack[sp-1]; sp++;");break;
   case IrPop:b.AppendLine("    --sp;");break;
   case IrAdd:b.AppendLine("    { intptr_t r=stack[--sp], l=stack[--sp]; stack[sp++]=l+r; }");break;
   case IrSub:b.AppendLine("    { intptr_t r=stack[--sp], l=stack[--sp]; stack[sp++]=l-r; }");break;
   case IrMul:b.AppendLine("    { intptr_t r=stack[--sp], l=stack[--sp]; stack[sp++]=l*r; }");break;
   case IrLoadField f:{var field=model.Fields[(f.TypeName,f.FieldName)];b.AppendLine($"    {{ DndObject *o=(DndObject*)stack[--sp]; stack[sp++]=*(int32_t*)((uint8_t*)o+sizeof(DndObject)+{field.Offset}); }}");break;}
   case IrStoreField f:{var field=model.Fields[(f.TypeName,f.FieldName)];b.AppendLine($"    {{ intptr_t v=stack[--sp]; DndObject *o=(DndObject*)stack[--sp]; *(int32_t*)((uint8_t*)o+sizeof(DndObject)+{field.Offset})=(int32_t)v; }}");break;}
   case IrNewObject n:{
    b.AppendLine($"    {{ intptr_t ca[{Math.Max(1,n.ArgumentCount+1)}]; for(int i={n.ArgumentCount};i>0;i--) ca[i]=stack[--sp]; DndObject *o=dnd_object_new(heap,&dnd_type_{Id(n.TypeName)}); ca[0]=(intptr_t)o;");
    b.AppendLine($"      dnd_method_{Id(n.Constructor.TypeName)}_{Id(n.Constructor.Name)}(heap,ca); stack[sp++]=(intptr_t)o; }}");break;}
   case IrCall c:{
    int total=c.ArgumentCount+(c.HasThis?1:0);b.AppendLine($"    {{ intptr_t ca[{Math.Max(1,total)}]; for(int i={total-1};i>=0;i--) ca[i]=stack[--sp]; intptr_t rv=dnd_method_{Id(c.Target.TypeName)}_{Id(c.Target.Name)}(heap,ca);{(c.ReturnsValue?" stack[sp++]=rv;":"")} }}");break;}
   case IrReturn r:b.AppendLine(r.HasValue?"    return stack[--sp];":"    return 0;");break;
  }
 }
 private static string Id(string s)=>new(s.Select(c=>char.IsLetterOrDigit(c)?c:'_').ToArray());
}
