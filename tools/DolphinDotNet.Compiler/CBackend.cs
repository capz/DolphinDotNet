using System.Text;
namespace DolphinDotNet.Compiler;
internal static class CBackend{
 public static string Emit(IEnumerable<IrMethod> methods,CompilationModel model,DependencyGraph graph){var b=new StringBuilder();b.AppendLine("/* DolphinDotNet closed-world AOT output. */");b.AppendLine("#include \"dnd_managed.h\"");b.AppendLine();foreach(var typeName in graph.Types.OrderBy(x=>x)){if(!model.Types.TryGetValue(typeName,out var t))continue;var id=Id(typeName);b.AppendLine($"static const DndType dnd_type_{id} = {{\"{typeName}\", &DND_TYPE_OBJECT, sizeof(DndObject)+{t.InstanceSize}u, 0, NULL}};");}foreach(var m in methods){b.AppendLine($"static void dnd_method_{Id(m.Key.TypeName)}_{Id(m.Key.Name)}(void) {{");foreach(var i in m.Instructions)b.AppendLine("    "+Line(i));b.AppendLine("}");}return b.ToString();}
 private static string Line(IrInstruction i)=>i switch{IrNewObject n=>$"/* newobj {n.TypeName}: dnd_object_new */",IrLoadField f=>$"/* ldfld {f.TypeName}.{f.FieldName} */",IrStoreField f=>$"/* stfld {f.TypeName}.{f.FieldName} */",IrCall c=>$"/* {(c.Virtual?"callvirt":"call")} {c.Target} */",IrConstI4 c=>$"/* ldc.i4 {c.Value} */",IrLoadArg a=>$"/* ldarg {a.Index} */",IrAdd=>"/* add */",IrSub=>"/* sub */",IrMul=>"/* mul */",IrReturn=>"return;",_=>"/* IR */"};
 private static string Id(string s)=>new(s.Select(c=>char.IsLetterOrDigit(c)?c:'_').ToArray());
}
