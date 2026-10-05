using System.Text;
namespace DolphinDotNet.Compiler;

internal static class ValueCBackend
{
    public static string EmitProgram(IReadOnlyList<ValueIrMethod> methods,MethodKey entry,CompilationModel model,DependencyGraph graph)
    {
        var b=new StringBuilder();b.AppendLine("#include <stdint.h>\n#include \"dnd_managed.h\"\n#include \"dnd_console.h\"\n#include \"dnd_input.h\"\n#include \"dnd_graphics.h\"\nstatic DndManagedHeap *dnd_value_heap;");
        var compiledKeys=methods.Select(m=>m.Key).ToHashSet();
        var virtualMethods=methods.Where(m=>model.Methods.TryGetValue(m.Key,out var mm)&&mm.IsVirtual).ToArray();
        foreach(var method in virtualMethods)b.AppendLine($"static intptr_t {WrapperSymbol(method.Key)}(intptr_t *args);");
        var interfaceTypes=graph.Types.Where(t=>model.Types.TryGetValue(t,out var tm)&&tm.IsInterface).OrderBy(x=>x).ToArray();
        foreach(var iface in interfaceTypes)b.AppendLine($"extern const DndType dnd_type_{Id(iface)};");
        foreach(var tn in graph.Types.OrderBy(x=>x))
        {
            var slots=VirtualSlots(tn,model,compiledKeys);
            if(slots.Count>0)b.AppendLine($"static const DndManagedMethod dnd_vtable_{Id(tn)}[] = {{ {string.Join(", ",slots.Select(s=>WrapperSymbol(s.Key)))} }};");
        }
        foreach(var tn in graph.Types.OrderBy(x=>x))
        {
            if(!model.Types.TryGetValue(tn,out var t))continue;
            var refs=model.Fields.Values.Where(f=>f.DeclaringType==tn&&f.IsReference).OrderBy(f=>f.Offset).ToArray();
            if(refs.Length>0)b.AppendLine($"static const uint32_t dnd_refs_{Id(tn)}[] = {{ {string.Join(", ",refs.Select(r=>$"sizeof(DndObject)+{BasePayloadSize(tn,model)}u+{r.Offset}u"))} }};");
            var parent=t.BaseType!=null&&graph.Types.Contains(t.BaseType)&&model.Types.ContainsKey(t.BaseType)?$"&dnd_type_{Id(t.BaseType)}":"&DND_TYPE_OBJECT";
            var totalSize=TotalInstanceSize(tn,model);
            var slots=VirtualSlots(tn,model,compiledKeys);
            var interfaces=(t.Interfaces??Array.Empty<string>()).Where(i=>graph.Types.Contains(i)&&model.Types.ContainsKey(i)).ToArray();
            if(interfaces.Length>0)b.AppendLine($"static const DndType *const dnd_interfaces_{Id(tn)}[] = {{ {string.Join(", ",interfaces.Select(i=>$"&dnd_type_{Id(i)}"))} }};");
            var interfaceMaps=new List<string>();
            foreach(var iface in interfaces)
            {
                var implementations=InterfaceImplementations(tn,iface,model,compiledKeys);
                if(implementations.Count==0)continue;
                var methodsName=$"dnd_imethods_{Id(tn)}_{Id(iface)}";
                b.AppendLine($"static const DndManagedMethod {methodsName}[] = {{ {string.Join(", ",implementations.Select(m=>WrapperSymbol(m.Key)))} }};");
                interfaceMaps.Add($"{{ &dnd_type_{Id(iface)}, {implementations.Count}u, {methodsName} }}");
            }
            if(interfaceMaps.Count>0)b.AppendLine($"static const DndInterfaceEntry dnd_imap_{Id(tn)}[] = {{ {string.Join(", ",interfaceMaps)} }};");
            b.AppendLine($"const DndType dnd_type_{Id(tn)} = {{\"{tn}\", {parent}, sizeof(DndObject)+{totalSize}u, {interfaces.Length}u, {(interfaces.Length>0?$"dnd_interfaces_{Id(tn)}":"NULL")}, {refs.Length}u, {(refs.Length>0?$"dnd_refs_{Id(tn)}":"NULL")}, 0, {slots.Count}u, {(slots.Count>0?$"dnd_vtable_{Id(tn)}":"NULL")}, {interfaceMaps.Count}u, {(interfaceMaps.Count>0?$"dnd_imap_{Id(tn)}":"NULL")}}};");
        }
        var staticFields=model.Fields.Values.Where(f=>f.IsStatic&&graph.Types.Contains(f.DeclaringType)).OrderBy(f=>f.DeclaringType).ThenBy(f=>f.Name).ToArray();
        foreach(var field in staticFields)b.AppendLine($"static intptr_t {StaticSymbol(field)};");
        var initializedTypes=graph.Types.Where(t=>TypeInitializer(t,model) is not null).OrderBy(t=>t).ToArray();
        foreach(var type in initializedTypes){b.AppendLine($"static uint8_t dnd_cctor_state_{Id(type)};");b.AppendLine($"static void {EnsureSymbol(type)}(void);");}
        foreach(var m in methods)b.AppendLine($"intptr_t {Symbol(m.Key)}({Parameters(m)});");
        foreach(var m in methods)b.AppendLine(Emit(m,model,Symbol(m.Key),false));
        foreach(var type in initializedTypes)
        {
            var cctor=TypeInitializer(type,model)!;
            b.AppendLine($"static void {EnsureSymbol(type)}(void) {{ if(dnd_cctor_state_{Id(type)}==2) return; if(dnd_cctor_state_{Id(type)}==1) return; dnd_cctor_state_{Id(type)}=1; (void){Symbol(cctor.Key)}(); dnd_cctor_state_{Id(type)}=2; }}");
        }
        foreach(var m in virtualMethods)
        {
            var n=m.ParameterCount+(m.HasThis?1:0);
            b.Append($"static intptr_t {WrapperSymbol(m.Key)}(intptr_t *args) {{ ");
            if(m.ReturnsValue)b.Append("return ");else b.Append("(void)");
            b.Append($"{Symbol(m.Key)}({string.Join(", ",Enumerable.Range(0,n).Select(i=>$"args[{i}]"))});");
            if(!m.ReturnsValue)b.Append(" return 0;");
            b.AppendLine(" }");
        }
        foreach(var group in methods.GroupBy(m=>(m.Key.TypeName,m.Key.Name)).Where(g=>g.Count()==1)){var m=group.Single();var alias=LegacySymbol(m.Key);if(alias==Symbol(m.Key))continue;b.Append($"intptr_t {alias}({Parameters(m)}) {{ return {Symbol(m.Key)}(");b.Append(string.Join(", ",Enumerable.Range(0,m.ParameterCount+(m.HasThis?1:0)).Select(i=>$"a{i}")));b.AppendLine("); }");}
        var em=methods.Single(m=>m.Key==entry);
        b.AppendLine("intptr_t dnd_aot_entry(DndManagedHeap *heap) {");b.AppendLine("  dnd_value_heap=heap;");
        var staticRefs=staticFields.Where(f=>f.IsReference).ToArray();
        if(staticRefs.Length>0){b.AppendLine($"  DndObject **static_slots[{staticRefs.Length}] = {{ {string.Join(", ",staticRefs.Select(f=>$"(DndObject**)&{StaticSymbol(f)}"))} }};");b.AppendLine($"  DndGcFrame static_frame; dnd_gc_frame_push(&static_frame, static_slots, {staticRefs.Length});");}
        b.Append($"  intptr_t result = {Symbol(entry)}(");b.Append(string.Join(", ",Enumerable.Range(0,em.ParameterCount+(em.HasThis?1:0)).Select(i=>$"a{i}")));b.AppendLine(");");
        if(staticRefs.Length>0)b.AppendLine("  dnd_gc_frame_pop(&static_frame);");b.AppendLine("  return result;");b.AppendLine("}");
        b.AppendLine("intptr_t dnd_value_aot_entry(DndManagedHeap *heap) { return dnd_aot_entry(heap); }");return b.ToString();
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
        var roots=values.Where(v=>v.Kind==IrValueKind.ObjectReference).Select(v=>$"(DndObject**)&v{v.Id}").ToList();
        roots.AddRange(method.Locals.Where(l=>l.Kind==IrValueKind.ObjectReference).Select(l=>$"(DndObject**)&l{l.Index}"));
        if(method.HasThis)roots.Add("(DndObject**)&a0");
        if(roots.Count>0)
        {
            b.AppendLine($"  DndObject **gc_slots[{roots.Count}] = {{ {string.Join(", ",roots)} }};");
            b.AppendLine($"  DndGcFrame gc_frame; dnd_gc_frame_push(&gc_frame, gc_slots, {roots.Count});");
        }
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
                    case ValueIrAddressOfLocal x:b.AppendLine($"  v{x.Result.Id} = (intptr_t)&l{x.Index};");break;
                    case ValueIrAddressOfArgument x:b.AppendLine($"  v{x.Result.Id} = (intptr_t)&a{x.Index};");break;
                    case ValueIrStoreArgument x:b.AppendLine($"  a{x.Index} = v{x.Value.Id};");break;
                    case ValueIrLoadIndirect x:{var ct=x.Reference?"intptr_t":x.Size==1?"int8_t":x.Size==2?"int16_t":x.Size==8?"int64_t":"int32_t";b.AppendLine($"  v{x.Result.Id} = *({ct}*)v{x.Address.Id};");break;}
                    case ValueIrStoreIndirect x:{var ct=x.Reference?"intptr_t":x.Size==1?"int8_t":x.Size==2?"int16_t":x.Size==8?"int64_t":"int32_t";b.AppendLine($"  *({ct}*)v{x.Address.Id} = ({ct})v{x.Value.Id};");break;}
                    case ValueIrConvert x:b.AppendLine($"  v{x.Result.Id} = ({CType(x.Result.Kind)})v{x.Value.Id};");break;
                    case ValueIrBinary x:{var unsigned=x.Operation.EndsWith(".un",StringComparison.Ordinal);var op=Op(x.Operation);var l=unsigned?$"(uintptr_t)v{x.Left.Id}":$"v{x.Left.Id}";var r=unsigned?$"(uintptr_t)v{x.Right.Id}":$"v{x.Right.Id}";b.AppendLine($"  v{x.Result.Id} = {l} {op} {r};");break;}
                    case ValueIrCall x:
                    {
                        var args=string.Join(", ",x.Arguments.Select(a=>$"v{a.Id}"));
                        if(x.Interface)
                        {
                            var slot=InterfaceSlot(x.Target,model);
                            b.AppendLine($"  {{ intptr_t call_args[{Math.Max(1,x.Arguments.Count)}] = {{ {args} }}; DndManagedMethod target=dnd_interface_resolve((DndObject*)call_args[0], &dnd_type_{Id(x.Target.TypeName)}, {slot}u);");
                            b.AppendLine(x.Result is { } ir?$"    v{ir.Id} = target ? target(call_args) : 0; }}":$"    if(target) (void)target(call_args); }}");
                        }
                        else if(x.Virtual)
                        {
                            var slot=VirtualSlot(x.Target,model);
                            b.AppendLine($"  {{ intptr_t call_args[{Math.Max(1,x.Arguments.Count)}] = {{ {args} }}; DndManagedMethod target=dnd_virtual_resolve((DndObject*)call_args[0], {slot}u);");
                            b.AppendLine(x.Result is { } vr?$"    v{vr.Id} = target ? target(call_args) : 0; }}":$"    if(target) (void)target(call_args); }}");
                        }
                        else b.AppendLine(x.Result is { } r?$"  v{r.Id} = {Symbol(x.Target)}({args});":$"  (void){Symbol(x.Target)}({args});");
                        break;
                    }
                    case ValueIrNewObject x:{var args=string.Join(", ",new[]{$"(intptr_t)v{x.Result.Id}"}.Concat(x.Arguments.Select(a=>$"v{a.Id}")));if(HasTypeInitializer(x.TypeName,model))b.AppendLine($"  {EnsureSymbol(x.TypeName)}();");b.AppendLine($"  v{x.Result.Id} = (intptr_t)dnd_object_new(dnd_value_heap, &dnd_type_{Id(x.TypeName)});");b.AppendLine($"  (void){Symbol(x.Constructor)}({args});");break;}
                    case ValueIrLoadField x:{var field=model.Fields[(x.TypeName,x.FieldName)];var ct=field.IsReference?"intptr_t":FieldCType(field);b.AppendLine($"  if(dnd_require_object((DndObject*)v{x.Object.Id})) v{x.Result.Id} = *({ct}*)((uint8_t*)v{x.Object.Id}+sizeof(DndObject)+{BasePayloadSize(x.TypeName,model)}+{field.Offset});");break;}
                    case ValueIrStoreField x:{var field=model.Fields[(x.TypeName,x.FieldName)];var ct=field.IsReference?"intptr_t":FieldCType(field);b.AppendLine($"  if(dnd_require_object((DndObject*)v{x.Object.Id})) *({ct}*)((uint8_t*)v{x.Object.Id}+sizeof(DndObject)+{BasePayloadSize(x.TypeName,model)}+{field.Offset}) = ({ct})v{x.Value.Id};");break;}
                    case ValueIrLoadStaticField x:{var field=model.Fields[(x.TypeName,x.FieldName)];if(HasTypeInitializer(x.TypeName,model))b.AppendLine($"  {EnsureSymbol(x.TypeName)}();");b.AppendLine($"  v{x.Result.Id} = {StaticSymbol(field)};");break;}
                    case ValueIrStoreStaticField x:{var field=model.Fields[(x.TypeName,x.FieldName)];if(HasTypeInitializer(x.TypeName,model))b.AppendLine($"  {EnsureSymbol(x.TypeName)}();");b.AppendLine($"  {StaticSymbol(field)} = v{x.Value.Id};");break;}
                    case ValueIrTypeTest x:b.AppendLine($"  v{x.Result.Id} = (intptr_t){(x.ThrowOnFailure?"dnd_cast":"dnd_isinst")}((DndObject*)v{x.Object.Id}, {TypeExpr(x.TypeName)});");break;
                    case ValueIrStringLength x:b.AppendLine($"  v{x.Result.Id} = ((DndString*)v{x.String.Id})->length;");break;
                    case ValueIrBox x:
                        if(x.TypeName!="System.Int32")throw new NotSupportedException($"Boxing {x.TypeName} is not implemented.");
                        b.AppendLine($"  v{x.Result.Id} = (intptr_t)dnd_box_i32(dnd_value_heap, (int32_t)v{x.Value.Id});");break;
                    case ValueIrUnboxAny x:
                        if(x.TypeName!="System.Int32")throw new NotSupportedException($"Unboxing {x.TypeName} is not implemented.");
                        b.AppendLine($"  v{x.Result.Id} = dnd_unbox_i32((DndObject*)v{x.Object.Id});");break;
                    case ValueIrNewArray x:b.AppendLine($"  v{x.Result.Id} = (intptr_t)dnd_managed_array_new_typed(dnd_value_heap, (uint32_t)v{x.Length.Id}, {x.ElementSize}u, {TypeExpr(x.ElementType)}, {(x.ElementsAreReferences?"true":"false")});");break;
                    case ValueIrArrayElementAddress x:b.AppendLine($"  v{x.Result.Id} = (intptr_t)dnd_array_element_address((DndArray*)v{x.Array.Id}, (uint32_t)v{x.Index.Id});");break;
                    case ValueIrArrayLength x:b.AppendLine($"  v{x.Result.Id} = dnd_array_length((DndArray*)v{x.Array.Id});");break;
                    case ValueIrLoadElement x:b.AppendLine(x.Reference?$"  v{x.Result.Id} = (intptr_t)dnd_array_load_ref((DndArray*)v{x.Array.Id}, (uint32_t)v{x.Index.Id});":$"  v{x.Result.Id} = dnd_array_load_i32((DndArray*)v{x.Array.Id}, (uint32_t)v{x.Index.Id});");break;
                    case ValueIrStoreElement x:b.AppendLine(x.Reference?$"  (void)dnd_array_store_ref((DndArray*)v{x.Array.Id}, (uint32_t)v{x.Index.Id}, (DndObject*)v{x.Value.Id});":$"  (void)dnd_array_store_i32((DndArray*)v{x.Array.Id}, (uint32_t)v{x.Index.Id}, (int32_t)v{x.Value.Id});");break;
                    case ValueIrConsoleWriteLine x:b.AppendLine($"  {{ DndString *s=(DndString*)v{x.String.Id}; char text[256]; size_t n=s&&s->length<255?s->length:255; for(size_t i=0;i<n;i++) text[i]=(char)(s->chars[i]&0x7f); text[n]=0; dnd_console_write_line(text); }}");break;
                    case ValueIrReadButtonsDown x:b.AppendLine($"  dnd_input_poll(); {{ const DndGamePad *pad=dnd_input_gamepad((unsigned)v{x.Port.Id}); v{x.Result.Id}=pad?(intptr_t)pad->down:0; }}");break;
                    case ValueIrPresentDemoFrame x:b.AppendLine($"  dnd_graphics_begin_frame(0.025f,0.035f,0.06f,1.0f); dnd_graphics_draw_demo((float)v{x.Rotation.Id}); dnd_graphics_begin_overlay(); dnd_console_render(); dnd_graphics_end_frame();");break;
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
            case ValueIrReturn r:
                if(r.Value is { } v){b.AppendLine($"  {{ intptr_t return_value = v{v.Id};");if(HasRoots(method))b.AppendLine("    dnd_gc_frame_pop(&gc_frame);");b.AppendLine("    return return_value; }");}
                else {if(HasRoots(method))b.AppendLine("  dnd_gc_frame_pop(&gc_frame);");b.AppendLine("  return 0;");}
                break;
            case null:b.AppendLine("  return 0;");break;
        }
    }
    private static void Edge(StringBuilder b,ValueIrMethod method,int from,int to,string indent="  ")
    {
        var target=method.Blocks.Single(x=>x.Id==to);
        foreach(var phi in target.Instructions.OfType<ValueIrPhi>())
            if(phi.Inputs.TryGetValue(from,out var input))b.AppendLine($"{indent}v{phi.Result.Id} = v{input.Id};");
    }
    internal static string Symbol(MethodKey k)=>"dnd_value_"+Id(k.AssemblyName)+"_"+Id(k.TypeName)+"_"+Id(k.Name)+"_"+StableId(k.Signature);
    private static int BasePayloadSize(string type,CompilationModel model){if(!model.Types.TryGetValue(type,out var t)||t.BaseType is not { } p||!model.Types.ContainsKey(p))return 0;return TotalInstanceSize(p,model);}
    private static int TotalInstanceSize(string type,CompilationModel model){if(!model.Types.TryGetValue(type,out var t))return 0;var parent=t.BaseType is { } p&&model.Types.ContainsKey(p)?TotalInstanceSize(p,model):0;return parent+t.InstanceSize;}
    private static MethodModel? TypeInitializer(string type,CompilationModel model)=>model.Methods.Values.FirstOrDefault(m=>m.Key.TypeName==type&&m.Key.Name==".cctor");
    private static bool HasTypeInitializer(string type,CompilationModel model)=>TypeInitializer(type,model) is not null;
    private static string EnsureSymbol(string type)=>"dnd_ensure_"+Id(type);
    private static IReadOnlyList<MethodModel> VirtualSlots(string type,CompilationModel model,HashSet<MethodKey> compiled)
    {
        var result=new List<MethodModel>();
        if(model.Types.TryGetValue(type,out var tm)&&tm.BaseType is { } parent&&model.Types.ContainsKey(parent))result.AddRange(VirtualSlots(parent,model,compiled));
        foreach(var method in model.Methods.Values.Where(m=>m.Key.TypeName==type&&m.IsVirtual&&compiled.Contains(m.Key)).OrderBy(m=>m.Handle.GetHashCode()))
        {
            var slot=result.FindIndex(x=>x.Key.Name==method.Key.Name&&x.Key.Signature==method.Key.Signature);
            if(slot>=0&&!method.IsNewSlot)result[slot]=method;else result.Add(method);
        }
        return result;
    }
    private static IReadOnlyList<MethodModel> InterfaceImplementations(string type,string iface,CompilationModel model,HashSet<MethodKey> compiled)
    {
        var methods=model.Methods.Values.Where(m=>m.Key.TypeName==iface&&m.IsVirtual).OrderBy(m=>m.Handle.GetHashCode()).ToArray();
        var result=new List<MethodModel>();
        foreach(var contract in methods)
        {
            MethodModel? implementation=null;var current=type;
            while(model.Types.ContainsKey(current))
            {
                implementation=model.Methods.Values.FirstOrDefault(m=>m.Key.TypeName==current&&!m.IsAbstract&&compiled.Contains(m.Key)&&m.Key.Name==contract.Key.Name&&m.Key.Signature==contract.Key.Signature);
                if(implementation is not null)break;
                current=model.Types[current].BaseType??"";
            }
            if(implementation is null)return Array.Empty<MethodModel>();
            result.Add(implementation);
        }
        return result;
    }
    private static int InterfaceSlot(MethodKey target,CompilationModel model)
    {
        var methods=model.Methods.Values.Where(m=>m.Key.TypeName==target.TypeName&&m.IsVirtual).OrderBy(m=>m.Handle.GetHashCode()).ToList();
        var slot=methods.FindIndex(m=>m.Key.Name==target.Name&&m.Key.Signature==target.Signature);
        if(slot<0)throw new NotSupportedException($"No interface slot for {target}.");return slot;
    }
    private static int VirtualSlot(MethodKey target,CompilationModel model)
    {
        var compiled=model.Methods.Keys.ToHashSet();var slots=VirtualSlots(target.TypeName,model,compiled);
        var slot=slots.ToList().FindIndex(x=>x.Key.Name==target.Name&&x.Key.Signature==target.Signature);
        if(slot<0)throw new NotSupportedException($"No virtual slot for {target}.");return slot;
    }
    private static string WrapperSymbol(MethodKey k)=>"dnd_wrap_"+Id(k.AssemblyName)+"_"+Id(k.TypeName)+"_"+Id(k.Name)+"_"+StableId(k.Signature);
    private static string FieldCType(FieldModel f)=>f.Size switch{1=>"int8_t",2=>"int16_t",8=>"int64_t",_=>"int32_t"};
    private static string StaticSymbol(FieldModel f)=>"dnd_static_"+Id(f.DeclaringType)+"_"+Id(f.Name);
    private static string TypeExpr(string type)=>type switch{"System.String"=>"&DND_TYPE_STRING","System.Object"=>"&DND_TYPE_OBJECT",_ when type.StartsWith("System.",StringComparison.Ordinal)=>"NULL",_=>$"&dnd_type_{Id(type)}"};
    private static string LegacySymbol(MethodKey k)=>"dnd_value_"+Id(k.TypeName)+"_"+Id(k.Name);
    private static string StableId(string s){uint h=2166136261;foreach(var ch in s){h^=ch;h*=16777619;}return h.ToString("x8");}
    private static string Parameters(ValueIrMethod m){var n=m.ParameterCount+(m.HasThis?1:0);return n==0?"void":string.Join(", ",Enumerable.Range(0,n).Select(i=>$"intptr_t a{i}"));}
    private static string Id(string s)=>new(s.Select(ch=>char.IsLetterOrDigit(ch)?ch:'_').ToArray());
    private static string Escape(string s)=>s.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\n","\\n").Replace("\r","\\r").Replace("\t","\\t");
    private static bool HasRoots(ValueIrMethod m)=>Collect(m).Any(v=>v.Kind==IrValueKind.ObjectReference)||m.Locals.Any(l=>l.Kind==IrValueKind.ObjectReference)||m.HasThis;
    private static string CType(IrValueKind kind)=>kind switch
    {
        IrValueKind.R4=>"float",
        IrValueKind.R8=>"double",
        IrValueKind.I8=>"int64_t",
        IrValueKind.ObjectReference or IrValueKind.ManagedPointer or IrValueKind.NativeInt=>"intptr_t",
        _=>"int32_t"
    };
    private static string Op(string op)=>op switch{"add"=>"+","sub"=>"-","mul"=>"*","and"=>"&","ceq"=>"==","cgt" or "cgt.un"=>">","clt" or "clt.un"=>"<",_=>throw new InvalidDataException($"Unsupported Value IR binary operation {op}.")};
    private static IEnumerable<IrValue> Collect(ValueIrMethod m)
    {
        foreach(var b in m.Blocks)foreach(var i in b.Instructions)switch(i)
        {
            case ValueIrCall x:if(x.Result is { } cr)yield return cr;foreach(var a in x.Arguments)yield return a;break;case ValueIrNewObject x:yield return x.Result;foreach(var a in x.Arguments)yield return a;break;case ValueIrLoadField x:yield return x.Result;yield return x.Object;break;case ValueIrStoreField x:yield return x.Object;yield return x.Value;break;case ValueIrLoadStaticField x:yield return x.Result;break;case ValueIrStoreStaticField x:yield return x.Value;break;case ValueIrLoadString x:yield return x.Result;break;case ValueIrTypeTest x:yield return x.Result;yield return x.Object;break;case ValueIrStringLength x:yield return x.Result;yield return x.String;break;case ValueIrBox x:yield return x.Result;yield return x.Value;break;case ValueIrUnboxAny x:yield return x.Result;yield return x.Object;break;case ValueIrNewArray x:yield return x.Result;yield return x.Length;break;case ValueIrArrayElementAddress x:yield return x.Result;yield return x.Array;yield return x.Index;break;case ValueIrArrayLength x:yield return x.Result;yield return x.Array;break;case ValueIrLoadElement x:yield return x.Result;yield return x.Array;yield return x.Index;break;case ValueIrStoreElement x:yield return x.Array;yield return x.Index;yield return x.Value;break;case ValueIrConsoleWriteLine x:yield return x.String;break;case ValueIrReadButtonsDown x:yield return x.Result;yield return x.Port;break;case ValueIrPresentDemoFrame x:yield return x.Rotation;break;case ValueIrConstant x:yield return x.Result;break;case ValueIrLoadArgument x:yield return x.Result;break;case ValueIrLoadLocal x:yield return x.Result;break;case ValueIrStoreLocal x:yield return x.Value;break;case ValueIrAddressOfLocal x:yield return x.Result;break;case ValueIrAddressOfArgument x:yield return x.Result;break;case ValueIrStoreArgument x:yield return x.Value;break;case ValueIrLoadIndirect x:yield return x.Result;yield return x.Address;break;case ValueIrStoreIndirect x:yield return x.Address;yield return x.Value;break;case ValueIrConvert x:yield return x.Result;yield return x.Value;break;case ValueIrBinary x:yield return x.Result;break;case ValueIrPhi x:yield return x.Result;foreach(var v in x.Inputs.Values)yield return v;break;case ValueIrOpaqueStackEffect x:foreach(var v in x.Results)yield return v;break;
        }
        foreach(var b in m.Blocks){foreach(var v in b.EntryStack.Values)yield return v;foreach(var v in b.ExitStack.Values)yield return v;if(b.Terminator is ValueIrBranch br){yield return br.Left;if(br.Right is { } r)yield return r;}else if(b.Terminator is ValueIrSwitch sw)yield return sw.Value;else if(b.Terminator is ValueIrReturn ret&&ret.Value is { } rv)yield return rv;}
    }
}
