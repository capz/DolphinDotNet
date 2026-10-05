using System.Text;
namespace DolphinDotNet.Compiler;
internal static class CBackend{
 public static string Emit(IEnumerable<IrMethod> source,CompilationModel model,DependencyGraph graph){
  var methods=source.ToList();var b=new StringBuilder();b.AppendLine("/* DolphinDotNet closed-world AOT output. */\n#include <stdint.h>\n#include <string.h>\n#include \"dnd_managed.h\"\n#include \"dnd_console.h\"\n#include \"dnd_input.h\"");
  foreach(var tn in graph.Types.OrderBy(x=>x)){if(!model.Types.TryGetValue(tn,out var t))continue;var parent=t.BaseType!=null&&graph.Types.Contains(t.BaseType)&&model.Types.ContainsKey(t.BaseType)?$"&dnd_type_{Id(t.BaseType)}":"&DND_TYPE_OBJECT";b.AppendLine($"const DndType dnd_type_{Id(tn)} = {{\"{tn}\", {parent}, sizeof(DndObject)+{t.InstanceSize}u, 0, NULL}};");}
  foreach(var m in methods)b.AppendLine($"static intptr_t {MethodSymbol(m.Key)}(DndManagedHeap*, intptr_t*);");
  foreach(var m in methods)EmitMethod(b,m,model);
  var entry=methods.FirstOrDefault(m=>m.Key.Name=="Main");
  if(entry!=null)b.AppendLine($"intptr_t dnd_aot_entry(DndManagedHeap *heap) {{ return {MethodSymbol(entry.Key)}(heap, NULL); }}");
  return b.ToString();
 }
 private static void EmitMethod(StringBuilder b,IrMethod m,CompilationModel model){
  string fn=MethodSymbol(m.Key);b.AppendLine($"static intptr_t {fn}(DndManagedHeap *heap, intptr_t *args) {{");b.AppendLine("    (void)heap; (void)args;");b.AppendLine("    intptr_t stack[64]; int sp=0;");b.AppendLine($"    intptr_t locals[{Math.Max(1,m.LocalCount)}]; memset(locals,0,sizeof(locals));");b.AppendLine("    DndObject *gc_objects[32]; size_t gc_count=0; DndGcFrame gc_frame; (void)gc_count; dnd_gc_frame_push(&gc_frame,gc_objects,0);");
  var branchTargets=m.Instructions.OfType<IrBranch>().Select(x=>x.TargetOffset).Concat(m.Instructions.OfType<IrSwitch>().SelectMany(x=>x.TargetOffsets)).ToHashSet();
  foreach(var i in m.Instructions){if(i is IrLabel l&&!branchTargets.Contains(l.Offset))continue;Emit(b,i,model);}b.AppendLine("    dnd_gc_frame_pop(&gc_frame); return 0;\n}");
 }
 private static void Emit(StringBuilder b,IrInstruction i,CompilationModel model){
  switch(i){
   case IrConstI4 x:b.AppendLine($"    stack[sp++] = (intptr_t){x.Value};");break;
   case IrLoadString s:{var lit=Escape(s.Value);b.AppendLine($"    {{ DndString *str=dnd_string_from_utf8(heap,\"{lit}\"); gc_objects[gc_count++]=(DndObject*)str; gc_frame.count=gc_count; stack[sp++]=(intptr_t)str; }}");break;}
   case IrLoadArg x:b.AppendLine($"    stack[sp++] = args[{x.Index}];");break;
   case IrStoreArg x:b.AppendLine($"    args[{x.Index}] = stack[--sp];");break;
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
    b.AppendLine($"      gc_objects[gc_count++]=o; gc_frame.count=gc_count; {MethodSymbol(n.Constructor)}(heap,ca); stack[sp++]=(intptr_t)o; }}");break;}
   case IrStringLength:b.AppendLine("    { DndString *s=(DndString*)stack[--sp]; stack[sp++]=s?(intptr_t)s->length:0; }");break;
   case IrConsoleWriteLine:b.AppendLine("    { DndString *s=(DndString*)stack[--sp]; char text[256]; size_t n=s&&s->length<255?s->length:255; for(size_t i=0;i<n;i++) text[i]=(char)(s->chars[i]&0x7f); text[n]=0; dnd_console_write_line(text); }");break;
   case IrReadButtonsDown:b.AppendLine("    { unsigned port=(unsigned)stack[--sp]; dnd_input_poll(); const DndGamePad *pad=dnd_input_gamepad(port); stack[sp++]=pad?(intptr_t)pad->down:0; }");break;
   case IrCompareGreaterThan:b.AppendLine("    { intptr_t r=stack[--sp], l=stack[--sp]; stack[sp++]=l>r?1:0; }");break;
   case IrCompareEqual:b.AppendLine("    { intptr_t r=stack[--sp], l=stack[--sp]; stack[sp++]=l==r?1:0; }");break;
   case IrLabel l:b.AppendLine($"dnd_il_{l.Offset:x4}: ;");break;
   case IrBranch br:{
    if(br.Condition==IrBranchCondition.Always){b.AppendLine($"    goto dnd_il_{br.TargetOffset:x4};");break;}
    if(br.Condition is IrBranchCondition.True or IrBranchCondition.False){
     var test=br.Condition==IrBranchCondition.True?"v != 0":"v == 0";
     b.AppendLine($"    {{ intptr_t v=stack[--sp]; if({test}) goto dnd_il_{br.TargetOffset:x4}; }}");break;
    }
    var op=br.Condition switch{IrBranchCondition.Equal=>"==",IrBranchCondition.NotEqual=>"!=",IrBranchCondition.GreaterThan=>">",IrBranchCondition.GreaterOrEqual=>">=",IrBranchCondition.LessThan=>"<",IrBranchCondition.LessOrEqual=>"<=",_=>throw new InvalidOperationException()};
    var cast=br.Unsigned?"uintptr_t":"intptr_t";
    b.AppendLine($"    {{ {cast} r=({cast})stack[--sp], l=({cast})stack[--sp]; if(l {op} r) goto dnd_il_{br.TargetOffset:x4}; }}");break;
   }
   case IrSwitch sw:{b.AppendLine("    { intptr_t v=stack[--sp]; switch(v) {");for(var si=0;si<sw.TargetOffsets.Count;si++)b.AppendLine($"      case {si}: goto dnd_il_{sw.TargetOffsets[si]:x4};");b.AppendLine("      default: break; } }");break;}
   case IrCall c:{
    int total=c.ArgumentCount+(c.HasThis?1:0);b.AppendLine($"    {{ intptr_t ca[{Math.Max(1,total)}]; for(int i={total-1};i>=0;i--) ca[i]=stack[--sp]; intptr_t rv={MethodSymbol(c.Target)}(heap,ca);{(c.ReturnsValue?" stack[sp++]=rv;":"")} }}");break;}
   case IrReturn r:b.AppendLine(r.HasValue?"    { intptr_t rv=stack[--sp]; dnd_gc_frame_pop(&gc_frame); return rv; }":"    dnd_gc_frame_pop(&gc_frame); return 0;");break;
  }
 }
 private static string MethodSymbol(MethodKey k)=>"dnd_method_"+Id(k.AssemblyName)+"_"+Id(k.TypeName)+"_"+Id(k.Name)+"_"+Id(k.Signature);
 private static string Escape(string s)=>s.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\n","\\n").Replace("\r","\\r").Replace("\t","\\t");
 private static string Id(string s)=>new(s.Select(c=>char.IsLetterOrDigit(c)?c:'_').ToArray());
}
