using System.Text;
namespace DolphinDotNet.Compiler;

internal static class ValueCBackend
{
    public static string EmitProgram(IReadOnlyList<ValueIrMethod> methods,MethodKey entry,CompilationModel model,DependencyGraph graph)
    {
        var b=new StringBuilder();b.AppendLine("#include <stdint.h>\n#include <string.h>\n#include \"dnd_managed.h\"\n#include \"dnd_console.h\"\n#include \"dnd_input.h\"\n#include \"dnd_graphics.h\"\nstatic DndManagedHeap *dnd_value_heap;");
        b.AppendLine("static inline int32_t dnd_read_i32(const void *p) { int32_t v; memcpy(&v,p,sizeof(v)); return v; }\nstatic inline int64_t dnd_read_i64(const void *p) { int64_t v; memcpy(&v,p,sizeof(v)); return v; }");
        var stringLiterals=methods.SelectMany(m=>m.Blocks).SelectMany(block=>block.Instructions).OfType<ValueIrLoadString>().Select(x=>x.Value).Distinct(StringComparer.Ordinal).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
        var stringIds=stringLiterals.Select((value,index)=>(value,index)).ToDictionary(x=>x.value,x=>x.index,StringComparer.Ordinal);
        foreach(var literal in stringLiterals)
        {
            var id=stringIds[literal];var units=literal.Select(ch=>$"0x{(int)ch:x4}u").Concat(new[]{"0u"});
            b.AppendLine($"static const struct {{ DndObject object; uint32_t length; uint16_t chars[{literal.Length+1}]; }} dnd_string_literal_{id} = {{ {{ &DND_TYPE_STRING, 0 }}, {literal.Length}u, {{ {string.Join(", ",units)} }} }};");
        }
        var compiledKeys=methods.Select(m=>m.Key).ToHashSet();
        var virtualMethods=methods.Where(m=>model.Methods.TryGetValue(m.Key,out var mm)&&mm.IsVirtual).ToArray();
        var functionTargets=methods.SelectMany(m=>m.Blocks).SelectMany(b=>b.Instructions).OfType<ValueIrLoadFunction>().Select(x=>x.Target).ToHashSet();
        var wrapperMethods=methods.Where(m=>model.Methods.TryGetValue(m.Key,out var mm)&& (mm.IsVirtual||functionTargets.Contains(m.Key))).ToArray();
        foreach(var method in wrapperMethods)b.AppendLine($"static intptr_t {WrapperSymbol(method.Key)}(intptr_t *args);");
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
            var interfaces=InterfaceClosure(tn,model).Where(i=>graph.Types.Contains(i)&&model.Types.ContainsKey(i)).ToArray();
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
        foreach(var m in methods)b.AppendLine($"{ReturnCType(m,model)} {Symbol(m.Key)}({Parameters(m,model)});");
        foreach(var m in methods)b.AppendLine(Emit(m,model,Symbol(m.Key),false,stringIds));
        foreach(var type in initializedTypes)
        {
            var cctor=TypeInitializer(type,model)!;
            b.AppendLine($"static void {EnsureSymbol(type)}(void) {{ if(dnd_cctor_state_{Id(type)}==2) return; if(dnd_cctor_state_{Id(type)}==1) return; dnd_cctor_state_{Id(type)}=1; (void){Symbol(cctor.Key)}(); dnd_cctor_state_{Id(type)}=2; }}");
        }
        foreach(var m in wrapperMethods)
        {
            var n=m.ParameterCount+(m.HasThis?1:0);
            b.Append($"static intptr_t {WrapperSymbol(m.Key)}(intptr_t *args) {{ ");
            if(m.ReturnsValue)b.Append("return ");else b.Append("(void)");
            b.Append($"{Symbol(m.Key)}({string.Join(", ",Enumerable.Range(0,n).Select(i=>$"args[{i}]"))});");
            if(!m.ReturnsValue)b.Append(" return 0;");
            b.AppendLine(" }");
        }
        foreach(var group in methods.GroupBy(m=>(m.Key.TypeName,m.Key.Name)).Where(g=>g.Count()==1)){var m=group.Single();var alias=LegacySymbol(m.Key);if(alias==Symbol(m.Key))continue;b.Append($"{ReturnCType(m,model)} {alias}({Parameters(m,model)}) {{ return {Symbol(m.Key)}(");b.Append(string.Join(", ",Enumerable.Range(0,m.ParameterCount+(m.HasThis?1:0)).Select(i=>$"a{i}")));b.AppendLine("); }");}
        var em=methods.Single(m=>m.Key==entry);
        b.AppendLine("intptr_t dnd_aot_entry(DndManagedHeap *heap) {");b.AppendLine("  dnd_value_heap=heap;");
        var staticRefs=staticFields.Where(f=>f.IsReference).ToArray();
        if(staticRefs.Length>0){b.AppendLine($"  DndObject **static_slots[{staticRefs.Length}] = {{ {string.Join(", ",staticRefs.Select(f=>$"(DndObject**)&{StaticSymbol(f)}"))} }};");b.AppendLine($"  DndGcFrame static_frame; dnd_gc_frame_push(&static_frame, static_slots, {staticRefs.Length});");}
        b.Append($"  intptr_t result = {Symbol(entry)}(");b.Append(string.Join(", ",Enumerable.Range(0,em.ParameterCount+(em.HasThis?1:0)).Select(i=>$"a{i}")));b.AppendLine(");");
        if(staticRefs.Length>0)b.AppendLine("  dnd_gc_frame_pop(&static_frame);");b.AppendLine("  return result;");b.AppendLine("}");
        b.AppendLine("intptr_t dnd_value_aot_entry(DndManagedHeap *heap) { return dnd_aot_entry(heap); }");return b.ToString();
    }

    public static string Emit(ValueIrMethod method,CompilationModel model,string functionName="dnd_value_ir_test",bool includeHeader=true,IReadOnlyDictionary<string,int>? stringIds=null)
    {
        var b=new StringBuilder();
        var values=Collect(method).GroupBy(v=>v.Id).Select(g=>g.First()).OrderBy(v=>v.Id).ToArray();
        if(includeHeader)b.AppendLine("#include <stdint.h>\n#include <string.h>");
        b.Append($"{ReturnCType(method,model)} {functionName}(");
        b.Append(Parameters(method,model));
        b.AppendLine(") {");
        for(var ai=0;ai<method.ParameterCount+(method.HasThis?1:0);ai++)b.AppendLine($"  (void)a{ai};");
        // Struct arguments travel as addresses, but are private value copies in
        // the callee. ldarga must address the copy, never the ABI pointer slot.
        var argumentStorage=model.Methods.TryGetValue(method.Key,out var signatureMethod)
            ?signatureMethod.Abi?.ParameterStorage:null;
        for(var pi=0;pi<(argumentStorage?.Count??0);pi++)
        {
            var storage=argumentStorage![pi]; if(storage.Size==0)continue;
            var ai=pi+(method.HasThis?1:0);
            b.AppendLine($"  _Alignas(8) uint8_t arg_{ai}[{storage.Size}]; memcpy(arg_{ai}, (void*)a{ai}, {storage.Size}u); a{ai}=(intptr_t)arg_{ai};");
        }
        foreach(var v in values)b.AppendLine($"  {(method.ExceptionRegions.Count>0?"volatile ":"")}{ValueStorageCType(v.Kind)} v{v.Id} = 0; (void)v{v.Id};");
        foreach(var local in method.Locals){b.AppendLine(local.StorageSize>0?$"  _Alignas(8) uint8_t l{local.Index}[{local.StorageSize}] = {{0}};":$"  {(method.ExceptionRegions.Count>0?"volatile ":"")}{CType(local.Kind)} l{local.Index} = 0;");b.AppendLine($"  (void)l{local.Index};");}
        var leavePaths=method.Blocks.Where(x=>x.Terminator is ValueIrLeave)
            .Select((x,index)=>(Block:x,Leave:(ValueIrLeave)x.Terminator!,Id:index)).ToArray();
        if(leavePaths.Length>0 || method.Blocks.Any(x=>x.Terminator is ValueIrEndFinally))b.AppendLine("  volatile int32_t dnd_leave_id = -1; volatile int32_t dnd_leave_step = 0; (void)dnd_leave_id; (void)dnd_leave_step;");
        var roots=values.Where(v=>v.Kind==IrValueKind.ObjectReference).Select(v=>$"(DndObject**)&v{v.Id}").ToList();
        roots.AddRange(method.Locals.Where(l=>l.Kind==IrValueKind.ObjectReference).Select(l=>$"(DndObject**)&l{l.Index}"));
        roots.AddRange(method.Locals.SelectMany(l=>(l.ReferenceOffsets??Array.Empty<int>()).Select(offset=>$"(DndObject**)(l{l.Index}+{offset})")));
        if(method.HasThis)roots.Add("(DndObject**)&a0");
        /* Incoming managed references must remain roots even before their first
           IL load. Callees can allocate while the argument is still live. */
        if(model.Methods.TryGetValue(method.Key,out var rootMethod) && rootMethod.Abi is { } rootAbi)
            for(var pi=0;pi<method.ParameterCount && pi<rootAbi.Parameters.Count;pi++)
                if(rootAbi.Parameters[pi]==CilStackKind.ObjectReference)
                    roots.Add($"(DndObject**)&a{pi+(method.HasThis?1:0)}");
        if(argumentStorage is not null)
            for(var pi=0;pi<argumentStorage.Count;pi++)
                foreach(var offset in argumentStorage[pi].ReferenceOffsets??Array.Empty<int>())
                    roots.Add($"(DndObject**)(arg_{pi+(method.HasThis?1:0)}+{offset})");
        if(roots.Count>0)
        {
            b.AppendLine($"  DndObject **gc_slots[{roots.Count}] = {{ {string.Join(", ",roots)} }};");
            b.AppendLine($"  DndGcFrame gc_frame; dnd_gc_frame_push(&gc_frame, gc_slots, {roots.Count});");
        }
        if(method.ExceptionRegions.Count>0)
        {
            b.AppendLine("  volatile int32_t dnd_eh_site = -1; volatile int32_t dnd_eh_unwind_finally_length = -1; volatile int dnd_eh_active = 1; DndEhFrame dnd_eh_frame; (void)dnd_eh_unwind_finally_length;");
            b.AppendLine("  dnd_eh_push(&dnd_eh_frame);");
            b.AppendLine("  if (setjmp(dnd_eh_frame.environment) != 0) {");
            foreach(var region in method.ExceptionRegions.OrderBy(r=>r.TryLength).ThenBy(r=>r.Kind==ValueIrExceptionRegionKind.Catch?0:1))
            {
                var handler=method.Blocks.FirstOrDefault(x=>x.CilOffset==region.HandlerOffset);
                if(handler is null)continue;
                if(region.Kind==ValueIrExceptionRegionKind.Finally)
                    b.AppendLine($"    if (dnd_eh_site >= {region.TryOffset} && dnd_eh_site < {region.TryOffset+region.TryLength} && dnd_eh_unwind_finally_length < {region.TryLength}) {{ dnd_eh_unwind_finally_length = {region.TryLength}; dnd_eh_push(&dnd_eh_frame); goto block_{handler.Id}; }}");
                else if(region.Kind==ValueIrExceptionRegionKind.Catch)
                {
                    var exceptionValue=handler.EntryStack.Values.FirstOrDefault();
                    var assign=handler.EntryStack.Values.Count>0?$"v{exceptionValue.Id} = (intptr_t)dnd_exception_object(); ":"";
                    var match=region.CatchType is null?"true":$"dnd_exception_matches({TypeExpr(region.CatchType)})";
                    b.AppendLine($"    if (dnd_eh_site >= {region.TryOffset} && dnd_eh_site < {region.TryOffset+region.TryLength} && {match}) {{ {assign}dnd_exception_begin_catch(); dnd_eh_site = {region.HandlerOffset}; dnd_eh_push(&dnd_eh_frame); goto block_{handler.Id}; }}");
                }
            }
            b.AppendLine("    if (dnd_eh_active) { dnd_eh_pop(&dnd_eh_frame); dnd_eh_active = 0; }");
            if(roots.Count>0)b.AppendLine("    dnd_gc_frame_pop(&gc_frame);");
            b.AppendLine("    dnd_exception_rethrow(); return 0;");
            b.AppendLine("  }");
        }
        if(method.Blocks.Count>0)b.AppendLine($"  goto block_{method.Blocks[0].Id};");
        foreach(var block in method.Blocks)
        {
            b.AppendLine($"block_{block.Id}:");
            if(method.ExceptionRegions.Count>0)
            {
                var protectedHere=method.ExceptionRegions.Any(r=>block.CilOffset>=r.TryOffset&&block.CilOffset<r.TryOffset+r.TryLength);
                b.AppendLine(protectedHere?$"  if (!dnd_exception_pending()) dnd_eh_site = {block.CilOffset};":"  if (!dnd_exception_pending()) dnd_eh_site = -1;");
            }
            foreach(var i in block.Instructions)
            {
                switch(i)
                {
                    case ValueIrConstant x:b.AppendLine($"  v{x.Result.Id} = {x.Value};");break;
                    case ValueIrLoadString x:b.AppendLine(stringIds is not null&&stringIds.TryGetValue(x.Value,out var sid)?$"  v{x.Result.Id} = (intptr_t)&dnd_string_literal_{sid};":$"  v{x.Result.Id} = (intptr_t)dnd_string_from_utf8(dnd_value_heap, \"{Escape(x.Value)}\");");break;
                    case ValueIrLoadArgument x:b.AppendLine($"  v{x.Result.Id} = a{x.Index};");break;
                    case ValueIrLoadLocal x:b.AppendLine($"  v{x.Result.Id} = l{x.Index};");break;
                    case ValueIrStoreLocal x:b.AppendLine($"  l{x.Index} = v{x.Value.Id};");break;
                    case ValueIrStoreLocalStruct x:b.AppendLine($"  memcpy(l{x.Index}, (void*)v{x.SourceAddress.Id}, {x.Size}u);");break;
                    case ValueIrAddressOfLocal x:b.AppendLine($"  v{x.Result.Id} = (intptr_t)&l{x.Index};");break;
                    case ValueIrNullableInit x:
                    {
                        var ct=x.ValueSize==8?"int64_t":"int32_t";
                        b.AppendLine($"  *(uint8_t*)v{x.Address.Id} = 1; {{ {ct} value=({ct})v{x.Value.Id}; memcpy((uint8_t*)v{x.Address.Id}+4,&value,sizeof(value)); }}");break;
                    }
                    case ValueIrNullableHasValue x:b.AppendLine($"  v{x.Result.Id} = *(uint8_t*)v{x.Address.Id} != 0;");break;
                    case ValueIrNullableGetValue x:
                    {
                        var ct=x.ValueSize==8?"int64_t":"int32_t";
                        if(x.ThrowIfEmpty)b.AppendLine($"  if(!*(uint8_t*)v{x.Address.Id}) dnd_exception_throw(DND_EXCEPTION_INVALID_OPERATION, \"Nullable object must have a value.\");");
                        b.AppendLine($"  v{x.Result.Id} = *(uint8_t*)v{x.Address.Id} ? {(x.ValueSize==8?"dnd_read_i64":"dnd_read_i32")}((uint8_t*)v{x.Address.Id}+4) : 0;");break;
                    }
                    case ValueIrAddressOfArgument x:
                    {
                        var pi=x.Index-(method.HasThis?1:0);
                        var aggregate=argumentStorage is not null&&pi>=0&&pi<argumentStorage.Count&&argumentStorage[pi].Size>0;
                        b.AppendLine(aggregate?$"  v{x.Result.Id} = a{x.Index};":$"  v{x.Result.Id} = (intptr_t)&a{x.Index};");break;
                    }
                    case ValueIrStoreArgument x:b.AppendLine($"  a{x.Index} = v{x.Value.Id};");break;
                    case ValueIrLoadIndirect x:{var ct=x.Reference?"intptr_t":x.Size==1?"int8_t":x.Size==2?"int16_t":x.Size==8?"int64_t":"int32_t";b.AppendLine($"  v{x.Result.Id} = *({ct}*)v{x.Address.Id};");break;}
                    case ValueIrStoreIndirect x:{var ct=x.Reference?"intptr_t":x.Size==1?"int8_t":x.Size==2?"int16_t":x.Size==8?"int64_t":"int32_t";b.AppendLine($"  *({ct}*)v{x.Address.Id} = ({ct})v{x.Value.Id};");break;}
                    case ValueIrInitObject x:b.AppendLine($"  memset((void*)v{x.Address.Id}, 0, {ValueTypeSize(x.TypeName,model)}u);");break;
                    case ValueIrCopyObject x:b.AppendLine($"  memcpy((void*)v{x.Destination.Id}, (void*)v{x.Source.Id}, {(x.Size>0?x.Size:ValueTypeSize(x.TypeName,model))}u);");break;
                    case ValueIrConvert x:b.AppendLine($"  v{x.Result.Id} = ({CType(x.Result.Kind)})v{x.Value.Id};");break;
                    case ValueIrBinary x:{var unsigned=x.Operation.EndsWith(".un",StringComparison.Ordinal);var op=Op(x.Operation);var l=unsigned?$"(uintptr_t)v{x.Left.Id}":$"v{x.Left.Id}";var r=unsigned?$"(uintptr_t)v{x.Right.Id}":$"v{x.Right.Id}";b.AppendLine($"  v{x.Result.Id} = {l} {op} {r};");break;}
                    case ValueIrObjectEquals x:
                    {
                        b.AppendLine($"  if (v{x.Left.Id} == v{x.Right.Id}) v{x.Result.Id}=1;");
                        b.AppendLine($"  else if (!v{x.Left.Id} || !v{x.Right.Id}) v{x.Result.Id}=0;");
                        b.AppendLine($"  else if (((DndObject*)v{x.Left.Id})->type != ((DndObject*)v{x.Right.Id})->type) v{x.Result.Id}=0;");
                        b.AppendLine($"  else if (((DndObject*)v{x.Left.Id})->type == &DND_TYPE_BOXED_INT32) v{x.Result.Id}=dnd_unbox_i32((DndObject*)v{x.Left.Id})==dnd_unbox_i32((DndObject*)v{x.Right.Id});");
                        b.AppendLine($"  else if (((DndObject*)v{x.Left.Id})->type == &DND_TYPE_INT64) v{x.Result.Id}=dnd_unbox_scalar((DndObject*)v{x.Left.Id},&DND_TYPE_INT64,8u)==dnd_unbox_scalar((DndObject*)v{x.Right.Id},&DND_TYPE_INT64,8u);");
                        b.AppendLine($"  else v{x.Result.Id}=0;");
                        break;
                    }
                    case ValueIrLoadFunction x:
                        if(x.Virtual&&x.Object is { } functionObject){var slot=VirtualSlot(x.Target,model);b.AppendLine($"  v{x.Result.Id} = (intptr_t)dnd_virtual_resolve((DndObject*)v{functionObject.Id}, {slot}u);");}
                        else b.AppendLine($"  v{x.Result.Id} = (intptr_t){WrapperSymbol(x.Target)};");
                        break;
                    case ValueIrNewDelegate x:b.AppendLine($"  v{x.Result.Id} = (intptr_t)dnd_managed_delegate_new(dnd_value_heap, (DndObject*)v{x.Target?.Id ?? -1}, (DndManagedMethod)v{x.Function.Id}, v{x.Target?.Id ?? -1} != 0);".Replace("v-1","0"));break;
                    case ValueIrDelegateInvoke x:
                    {
                        var invokeArgs=string.Join(", ",x.Arguments.Select(a=>$"v{a.Id}"));
                        b.AppendLine($"  {{ intptr_t delegate_args[{Math.Max(1,x.Arguments.Count)}] = {{ {invokeArgs} }};");
                        b.AppendLine(x.Result is { } dr?$"    v{dr.Id} = dnd_managed_delegate_invoke((DndDelegate*)v{x.Delegate.Id}, delegate_args, {x.Arguments.Count}u); }}":$"    (void)dnd_managed_delegate_invoke((DndDelegate*)v{x.Delegate.Id}, delegate_args, {x.Arguments.Count}u); }}");
                        break;
                    }
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
                    case ValueIrNewException x:b.AppendLine($"  v{x.Result.Id} = (intptr_t)dnd_exception_new(dnd_value_heap, {TypeExpr(x.TypeName)}, {(x.Message is { } m?$"(DndString*)v{m.Id}":"NULL")});");break;
                    case ValueIrExceptionMessage x:b.AppendLine($"  v{x.Result.Id} = (intptr_t)dnd_exception_get_message((DndException*)v{x.Exception.Id});");break;
                    case ValueIrLoadField x:{var field=model.Fields[(x.TypeName,x.FieldName)];var ct=field.IsReference?"intptr_t":FieldCType(field);b.AppendLine($"  if(dnd_require_object((DndObject*)v{x.Object.Id})) v{x.Result.Id} = *({ct}*)((uint8_t*)v{x.Object.Id}+sizeof(DndObject)+{BasePayloadSize(x.TypeName,model)}+{field.Offset});");break;}
                    case ValueIrStoreField x:{var field=model.Fields[(x.TypeName,x.FieldName)];var ct=field.IsReference?"intptr_t":FieldCType(field);b.AppendLine($"  if(dnd_require_object((DndObject*)v{x.Object.Id})) *({ct}*)((uint8_t*)v{x.Object.Id}+sizeof(DndObject)+{BasePayloadSize(x.TypeName,model)}+{field.Offset}) = ({ct})v{x.Value.Id};");break;}
                    case ValueIrLoadStaticField x:{var field=model.Fields[(x.TypeName,x.FieldName)];if(HasTypeInitializer(x.TypeName,model))b.AppendLine($"  {EnsureSymbol(x.TypeName)}();");b.AppendLine($"  v{x.Result.Id} = {StaticSymbol(field)};");break;}
                    case ValueIrStoreStaticField x:{var field=model.Fields[(x.TypeName,x.FieldName)];if(HasTypeInitializer(x.TypeName,model))b.AppendLine($"  {EnsureSymbol(x.TypeName)}();");b.AppendLine($"  {StaticSymbol(field)} = v{x.Value.Id};");break;}
                    case ValueIrTypeTest x:b.AppendLine($"  v{x.Result.Id} = (intptr_t){(x.ThrowOnFailure?"dnd_cast":"dnd_isinst")}((DndObject*)v{x.Object.Id}, {TypeExpr(x.TypeName)});");break;
                    case ValueIrStringLength x:b.AppendLine($"  v{x.Result.Id} = ((DndString*)v{x.String.Id})->length;");break;
                    case ValueIrPrimitiveToString x:
                    {
                        // Primitive instance ToString receives a managed address (ldloca/ldarga),
                        // not the numeric value itself. Dereference with the primitive's width.
                        var raw=$"v{x.Value.Id}";
                        var pointer=x.Value.Kind==IrValueKind.ManagedPointer;
                        var signed=x.TypeName is "System.SByte" or "System.Int16" or "System.Int32" or "System.Int64";
                        var width=x.TypeName switch {
                            "System.Boolean" or "System.Byte" or "System.SByte"=>8,
                            "System.Char" or "System.Int16" or "System.UInt16"=>16,
                            "System.Int64" or "System.UInt64"=>64,
                            _=>32
                        };
                        var numeric=pointer?$"(*({(signed?"int":"uint")}{width}_t*)(intptr_t){raw})":$"({(signed?"int":"uint")}{width}_t){raw}";
                        b.AppendLine(x.TypeName switch {
                            "System.Boolean"=>$"  v{x.Result.Id} = (intptr_t)dnd_string_from_bool(dnd_value_heap, {numeric} != 0);",
                            "System.Char"=>$"  v{x.Result.Id} = (intptr_t)dnd_string_from_char(dnd_value_heap, (uint16_t){numeric});",
                            "System.UInt32" or "System.UInt64" or "System.Byte" or "System.UInt16"=>$"  v{x.Result.Id} = (intptr_t)dnd_string_from_u64(dnd_value_heap, (uint64_t){numeric});",
                            _=>$"  v{x.Result.Id} = (intptr_t)dnd_string_from_i64(dnd_value_heap, (int64_t){numeric});"
                        });
                        break;
                    }
                    case ValueIrStringOperation x:
                    {
                        var a=x.Arguments;
                        var expression=x.Operation switch
                        {
                            "charAt"=>$"dnd_string_char_at((DndString*)v{a[0].Id}, (int32_t)v{a[1].Id})",
                            "StringEquals"=>$"dnd_string_equals((DndString*)v{a[0].Id}, (DndString*)v{a[1].Id})",
                            "StringStartsWith"=>$"dnd_string_starts_with((DndString*)v{a[0].Id}, (DndString*)v{a[1].Id})",
                            "StringEndsWith"=>$"dnd_string_ends_with((DndString*)v{a[0].Id}, (DndString*)v{a[1].Id})",
                            "StringContains"=>$"(dnd_string_index_of((DndString*)v{a[0].Id}, (DndString*)v{a[1].Id}) >= 0)",
                            "StringIndexOf"=>$"dnd_string_index_of((DndString*)v{a[0].Id}, (DndString*)v{a[1].Id})",
                            "StringConcat"=>$"(intptr_t)dnd_string_concat(dnd_value_heap, (DndString*)v{a[0].Id}, (DndString*)v{a[1].Id})",
                            "substring1"=>$"(intptr_t)dnd_string_substring(dnd_value_heap, (DndString*)v{a[0].Id}, (int32_t)v{a[1].Id}, (int32_t)(((DndString*)v{a[0].Id})->length-(uint32_t)v{a[1].Id}))",
                            "substring2"=>$"(intptr_t)dnd_string_substring(dnd_value_heap, (DndString*)v{a[0].Id}, (int32_t)v{a[1].Id}, (int32_t)v{a[2].Id})",
                            _=>throw new InvalidDataException($"Unsupported string operation {x.Operation}.")
                        };
                        b.AppendLine($"  v{x.Result.Id} = {expression};");break;
                    }
                    case ValueIrNullableGetValueOrDefault x:
                    {
                        var ct=x.ValueSize==8?"int64_t":"int32_t";
                        b.AppendLine($"  v{x.Result.Id} = *(uint8_t*)v{x.Address.Id} ? {(x.ValueSize==8?"dnd_read_i64":"dnd_read_i32")}((uint8_t*)v{x.Address.Id}+4) : ({ct})v{x.DefaultValue.Id};");break;
                    }
                    case ValueIrNullableEquals x:
                    {
                        var type=x.ValueSize==8?"&DND_TYPE_INT64":"&DND_TYPE_BOXED_INT32";var ct=x.ValueSize==8?"int64_t":"int32_t";
                        b.AppendLine($"  v{x.Result.Id} = !*(uint8_t*)v{x.Address.Id} ? (v{x.Other.Id}==0) : (v{x.Other.Id}!=0 && ((DndObject*)v{x.Other.Id})->type=={type} && {(x.ValueSize==8?"dnd_read_i64":"dnd_read_i32")}((uint8_t*)v{x.Address.Id}+4)==({ct})dnd_unbox_scalar((DndObject*)v{x.Other.Id},{type},{x.ValueSize}u));");break;
                    }
                    case ValueIrNullableHash x:
                    {
                        if(x.ValueSize==8)b.AppendLine($"  {{ uint64_t h=*(uint8_t*)v{x.Address.Id}?(uint64_t)dnd_read_i64((uint8_t*)v{x.Address.Id}+4):0; v{x.Result.Id}=(int32_t)(h^(h>>32)); }}");
                        else b.AppendLine($"  v{x.Result.Id} = *(uint8_t*)v{x.Address.Id} ? dnd_read_i32((uint8_t*)v{x.Address.Id}+4) : 0;");
                        break;
                    }
                    case ValueIrNewStruct x:{b.AppendLine($"  _Alignas(8) uint8_t struct_{x.Result.Id}[{x.Size}]={{0}}; v{x.Result.Id}=(intptr_t)struct_{x.Result.Id};");foreach(var f in x.Fields){var ct=f.Reference?"intptr_t":f.Size==8?"int64_t":f.Size==2?"int16_t":f.Size==1?"int8_t":"int32_t";b.AppendLine($"  {{ {ct} value=({ct})v{f.Value.Id}; memcpy(struct_{x.Result.Id}+{f.Offset},&value,sizeof(value)); }}");}break;}
                    case ValueIrStructStore x:{var ct=x.Reference?"intptr_t":x.Size==8?"int64_t":x.Size==2?"int16_t":x.Size==1?"int8_t":"int32_t";b.AppendLine($"  *({ct}*)((uint8_t*)v{x.Address.Id}+{x.Offset})=({ct})v{x.Value.Id};");break;}
                    case ValueIrStructLoad x:{var ct=x.Reference?"intptr_t":x.Size==8?"int64_t":x.Size==2?"int16_t":x.Size==1?"int8_t":"int32_t";b.AppendLine($"  v{x.Result.Id}=*({ct}*)((uint8_t*)v{x.Address.Id}+{x.Offset});");break;}
                    case ValueIrArraySegmentItem x:{var ct=x.Reference?"intptr_t":x.ElementSize==8?"int64_t":x.ElementSize==2?"int16_t":x.ElementSize==1?"int8_t":"int32_t";b.AppendLine($"  {{ DndArray *a=*(DndArray**)v{x.Address.Id}; int32_t base=*(int32_t*)((uint8_t*)v{x.Address.Id}+8); v{x.Result.Id}=*({ct}*)dnd_managed_array_at(a,(uint32_t)(base+(int32_t)v{x.Index.Id})); }}");break;}
                    case ValueIrArraySegmentGetEnumerator x:b.AppendLine($"  _Alignas(8) uint8_t enum_{x.Result.Id}[24]={{0}}; DndArray *a=*(DndArray**)v{x.Segment.Id}; int32_t start=*(int32_t*)((uint8_t*)v{x.Segment.Id}+8); int32_t count=*(int32_t*)((uint8_t*)v{x.Segment.Id}+12); *(DndArray**)enum_{x.Result.Id}=a; *(int32_t*)(enum_{x.Result.Id}+8)=start; *(int32_t*)(enum_{x.Result.Id}+12)=start+count; *(int32_t*)(enum_{x.Result.Id}+16)=start-1; v{x.Result.Id}=(intptr_t)enum_{x.Result.Id};");break;
                    case ValueIrEnumeratorMoveNext x:b.AppendLine($"  {{ int32_t *cur=(int32_t*)((uint8_t*)v{x.Address.Id}+16); int32_t end=*(int32_t*)((uint8_t*)v{x.Address.Id}+12); if(*cur < end) (*cur)++; v{x.Result.Id}=*cur < end; }}");break;
                    case ValueIrEnumeratorCurrent x:{var ct=x.Reference?"intptr_t":x.ElementSize==8?"int64_t":x.ElementSize==2?"int16_t":x.ElementSize==1?"int8_t":"int32_t";b.AppendLine($"  {{ DndArray *a=*(DndArray**)v{x.Address.Id}; int32_t cur=*(int32_t*)((uint8_t*)v{x.Address.Id}+16); v{x.Result.Id}=*({ct}*)dnd_managed_array_at(a,(uint32_t)cur); }}");break;}
                    case ValueIrBoxNullable x:
                    {
                        var type=x.ValueSize==8?"&DND_TYPE_INT64":"&DND_TYPE_BOXED_INT32";
                        b.AppendLine($"  v{x.Result.Id} = *(uint8_t*)v{x.Address.Id} ? (intptr_t)dnd_box_scalar(dnd_value_heap, {type}, (uint64_t){(x.ValueSize==8?"dnd_read_i64":"dnd_read_i32")}((uint8_t*)v{x.Address.Id}+4), {x.ValueSize}u) : 0;");
                        break;
                    }
                    case ValueIrBox x:
                        if(x.TypeName=="System.Int32")b.AppendLine($"  v{x.Result.Id} = (intptr_t)dnd_box_i32(dnd_value_heap, (int32_t)v{x.Value.Id});");
                        else if(IsScalarBoxType(x.TypeName))b.AppendLine($"  v{x.Result.Id} = (intptr_t)dnd_box_scalar(dnd_value_heap, {TypeExpr(x.TypeName)}, (uint64_t)v{x.Value.Id}, {ValueTypeSize(x.TypeName,model)}u);");
                        else throw new NotSupportedException($"Boxing {x.TypeName} is not implemented.");
                        break;
                    case ValueIrUnboxAny x:
                        if(x.TypeName=="System.Int32")b.AppendLine($"  v{x.Result.Id} = dnd_unbox_i32((DndObject*)v{x.Object.Id});");
                        else if(IsScalarBoxType(x.TypeName))b.AppendLine($"  v{x.Result.Id} = (intptr_t)dnd_unbox_scalar((DndObject*)v{x.Object.Id}, {TypeExpr(x.TypeName)}, {ValueTypeSize(x.TypeName,model)}u);");
                        else throw new NotSupportedException($"Unboxing {x.TypeName} is not implemented.");
                        break;
                    case ValueIrNewArray x:b.AppendLine($"  v{x.Result.Id} = (intptr_t)dnd_managed_array_new_typed(dnd_value_heap, (uint32_t)v{x.Length.Id}, {(x.ElementsAreReferences?"sizeof(DndObject*)":$"{x.ElementSize}u")}, {(x.ElementType=="$generic"?(x.ElementsAreReferences?"&DND_TYPE_OBJECT":"NULL"):TypeExpr(x.ElementType))}, {(x.ElementsAreReferences?"true":"false")});");break;
                    case ValueIrArrayElementAddress x:b.AppendLine($"  v{x.Result.Id} = (intptr_t)dnd_array_element_address((DndArray*)v{x.Array.Id}, (uint32_t)v{x.Index.Id});");break;
                    case ValueIrArrayLength x:b.AppendLine($"  v{x.Result.Id} = dnd_array_length((DndArray*)v{x.Array.Id});");break;
                    case ValueIrArrayOperation x:
                    {
                        var a=x.Arguments;
                        if(x.InterfaceTarget is not null)
                            b.AppendLine($"  if(v{a[0].Id} && ((DndObject*)v{a[0].Id})->type==&DND_TYPE_ARRAY) {{");
                        switch(x.Operation)
                        {
                            case "ArrayLength": b.AppendLine($"  v{x.Result.Value.Id} = dnd_array_length((DndArray*)v{a[0].Id});"); break;
                            case "ArrayLongLength": b.AppendLine($"  v{x.Result.Value.Id} = dnd_array_long_length((DndArray*)v{a[0].Id});"); break;
                            case "ArrayRank": b.AppendLine($"  (void)dnd_array_length((DndArray*)v{a[0].Id}); v{x.Result.Value.Id} = 1;"); break;
                            case "ArrayGetLength": b.AppendLine($"  v{x.Result.Value.Id} = dnd_array_get_length((DndArray*)v{a[0].Id}, (int32_t)v{a[1].Id});"); break;
                            case "ArrayGetLowerBound": b.AppendLine($"  v{x.Result.Value.Id} = dnd_array_get_lower_bound((DndArray*)v{a[0].Id}, (int32_t)v{a[1].Id});"); break;
                            case "ArrayGetUpperBound": b.AppendLine($"  v{x.Result.Value.Id} = dnd_array_get_upper_bound((DndArray*)v{a[0].Id}, (int32_t)v{a[1].Id});"); break;
                            case "ArrayClear": b.AppendLine($"  (void)dnd_array_clear((DndArray*)v{a[0].Id}, (int32_t)v{a[1].Id}, (int32_t)v{a[2].Id});"); break;
                            case "ArrayCopy":
                                if(a.Count==3)b.AppendLine($"  (void)dnd_array_copy((DndArray*)v{a[0].Id}, 0, (DndArray*)v{a[1].Id}, 0, (int32_t)v{a[2].Id});");
                                else b.AppendLine($"  (void)dnd_array_copy((DndArray*)v{a[0].Id}, (int32_t)v{a[1].Id}, (DndArray*)v{a[2].Id}, (int32_t)v{a[3].Id}, (int32_t)v{a[4].Id});");
                                break;
                            case "ArrayInterfaceCount": b.AppendLine($"  v{x.Result.Value.Id} = dnd_array_length((DndArray*)v{a[0].Id});"); break;
                            case "ArrayInterfaceIsReadOnly": b.AppendLine($"  v{x.Result.Value.Id} = 1;"); break;
                            case "ArrayInterfaceItemGet":
                                b.AppendLine(x.Reference?$"  v{x.Result.Value.Id} = (intptr_t)dnd_array_load_ref((DndArray*)v{a[0].Id}, (uint32_t)v{a[1].Id});":$"  v{x.Result.Value.Id} = ({CType(x.Result.Value.Kind)})dnd_array_load_scalar((DndArray*)v{a[0].Id}, (uint32_t)v{a[1].Id}, {x.ElementSize}u, false);"); break;
                            case "ArrayInterfaceItemSet":
                                b.AppendLine(x.Reference?$"  (void)dnd_array_store_ref((DndArray*)v{a[0].Id}, (uint32_t)v{a[1].Id}, (DndObject*)v{a[2].Id});":$"  (void)dnd_array_store_scalar((DndArray*)v{a[0].Id}, (uint32_t)v{a[1].Id}, (uint64_t)v{a[2].Id}, {x.ElementSize}u);"); break;
                            case "ArrayInterfaceContains":
                                b.AppendLine($"  v{x.Result.Value.Id} = dnd_array_index_of((DndArray*)v{a[0].Id}, (uint64_t)v{a[1].Id}, {x.ElementSize}u, {(x.Reference?"true":"false")}, 0, (int32_t)dnd_array_length((DndArray*)v{a[0].Id})) >= 0;"); break;
                            case "ArrayInterfaceIndexOf":
                                b.AppendLine($"  v{x.Result.Value.Id} = dnd_array_index_of((DndArray*)v{a[0].Id}, (uint64_t)v{a[1].Id}, {x.ElementSize}u, {(x.Reference?"true":"false")}, 0, (int32_t)dnd_array_length((DndArray*)v{a[0].Id}));"); break;
                            case "ArrayIndexOf":
                            {
                                var start=a.Count>2?$"(int32_t)v{a[2].Id}":"0";
                                var count=a.Count>3?$"(int32_t)v{a[3].Id}":$"(int32_t)dnd_array_length((DndArray*)v{a[0].Id}) - ({start})";
                                b.AppendLine($"  v{x.Result.Value.Id} = dnd_array_index_of((DndArray*)v{a[0].Id}, (uint64_t)v{a[1].Id}, {x.ElementSize}u, {(x.Reference?"true":"false")}, {start}, {count});");
                                break;
                            }
                        }
                        if(x.InterfaceTarget is { } targetKey)
                        {
                            var slot=InterfaceSlot(targetKey,model);
                            b.AppendLine($"  }} else {{ intptr_t call_args[{a.Count}] = {{ {string.Join(", ",a.Select(v=>$"v{v.Id}"))} }}; DndManagedMethod target=dnd_interface_resolve((DndObject*)call_args[0], &dnd_type_{Id(targetKey.TypeName)}, {slot}u);");
                            b.AppendLine(x.Result is { } result?$"    v{result.Id}=target?target(call_args):0; }}":$"    if(target) (void)target(call_args); }}");
                        }
                        break;
                    }

                    case ValueIrLoadElement x:b.AppendLine(x.Reference?$"  v{x.Result.Id} = (intptr_t)dnd_array_load_ref((DndArray*)v{x.Array.Id}, (uint32_t)v{x.Index.Id});":$"  v{x.Result.Id} = ({CType(x.Result.Kind)})dnd_array_load_scalar((DndArray*)v{x.Array.Id}, (uint32_t)v{x.Index.Id}, {x.Size}u, {(x.Signed?"true":"false")});");break;
                    case ValueIrStoreElement x:b.AppendLine(x.Reference?$"  (void)dnd_array_store_ref((DndArray*)v{x.Array.Id}, (uint32_t)v{x.Index.Id}, (DndObject*)v{x.Value.Id});":$"  (void)dnd_array_store_scalar((DndArray*)v{x.Array.Id}, (uint32_t)v{x.Index.Id}, (uint64_t)v{x.Value.Id}, {x.Size}u);");break;
                    case ValueIrConsoleWriteLine x:b.AppendLine($"  {{ DndString *s=(DndString*)v{x.String.Id}; char text[256]; size_t n=s&&s->length<255?s->length:255; for(size_t i=0;i<n;i++) text[i]=(char)(s->chars[i]&0x7f); text[n]=0; dnd_console_write_line(text); }}");break;
                    case ValueIrReadButtonsDown x:b.AppendLine($"  dnd_input_poll(); {{ const DndGamePad *pad=dnd_input_gamepad((unsigned)v{x.Port.Id}); v{x.Result.Id}=pad?(intptr_t)pad->down:0; }}");break;
                    case ValueIrPresentDemoFrame x:b.AppendLine($"  dnd_graphics_begin_frame(0.025f,0.035f,0.06f,1.0f); dnd_graphics_draw_demo((float)v{x.Rotation.Id}); dnd_graphics_begin_overlay(); dnd_console_render(); dnd_graphics_end_frame();");break;
                    case ValueIrPhi: break; // Assigned on predecessor edges.
                }
            }
            EmitTerminator(b,method,block,model,leavePaths.Select(x=>(x.Block.Id,x.Leave,x.Id)).ToArray());
        }
        b.AppendLine("}");
        return b.ToString();
    }

    private static void EmitTerminator(StringBuilder b,ValueIrMethod method,ValueIrBlock block,CompilationModel model,IReadOnlyList<(int BlockId,ValueIrLeave Leave,int Id)> leavePaths)
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
                if(r.Value is { } v){b.AppendLine($"  {{ intptr_t return_value = v{v.Id};");if(method.ExceptionRegions.Count>0)b.AppendLine("    if (dnd_eh_active) dnd_eh_pop(&dnd_eh_frame);");if(HasRoots(method,model))b.AppendLine("    dnd_gc_frame_pop(&gc_frame);");b.AppendLine("    return return_value; }");}
                else {if(method.ExceptionRegions.Count>0)b.AppendLine("  if (dnd_eh_active) dnd_eh_pop(&dnd_eh_frame);");if(HasRoots(method,model))b.AppendLine("  dnd_gc_frame_pop(&gc_frame);");b.AppendLine("  return 0;");}
                break;
            case ValueIrLeave leave:
            {
                var id=leavePaths.Single(x=>x.BlockId==block.Id).Id;
                if(leave.FinallyHandlers.Count==0){Edge(b,method,block.Id,leave.TargetBlock);b.AppendLine($"  goto block_{leave.TargetBlock};");}
                else
                {
                    var handler=method.Blocks.Single(x=>x.CilOffset==leave.FinallyHandlers[0]);
                    b.AppendLine($"  dnd_leave_id = {id}; dnd_leave_step = 0; goto block_{handler.Id};");
                }
                break;
            }
            case ValueIrEndFinally ef:
                b.AppendLine("  if (dnd_exception_pending()) { dnd_leave_id = -1; dnd_exception_rethrow(); return 0; }");
                b.AppendLine("  switch (dnd_leave_id) {");
                foreach(var path in leavePaths.Where(x=>x.Leave.FinallyHandlers.Contains(ef.HandlerOffset)))
                {
                    var step=path.Leave.FinallyHandlers.ToList().IndexOf(ef.HandlerOffset);
                    b.AppendLine($"    case {path.Id}:");
                    b.AppendLine($"      if (dnd_leave_step != {step}) break;");
                    if(step+1<path.Leave.FinallyHandlers.Count)
                    {
                        var next=method.Blocks.Single(x=>x.CilOffset==path.Leave.FinallyHandlers[step+1]);
                        b.AppendLine($"      dnd_leave_step++; goto block_{next.Id};");
                    }
                    else
                    {
                        b.AppendLine("      dnd_leave_id = -1;");
                        Edge(b,method,block.Id,path.Leave.TargetBlock,"      ");
                        b.AppendLine($"      goto block_{path.Leave.TargetBlock};");
                    }
                }
                b.AppendLine("    default: break;");
                b.AppendLine("  }");
                b.AppendLine("  dnd_exception_rethrow(); return 0;");
                break;
            case ValueIrThrow t:
                if(t.Exception is { } ex)b.AppendLine($"  dnd_exception_throw_object((DndObject*)v{ex.Id});");
                else b.AppendLine("  dnd_exception_rethrow_current();");
                b.AppendLine("  return 0;");
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
    private static int ValueTypeSize(string type,CompilationModel model)=>type switch{"System.Boolean" or "System.Byte" or "System.SByte"=>1,"System.Char" or "System.Int16" or "System.UInt16"=>2,"System.Int64" or "System.UInt64" or "System.Double"=>8,"System.Single" or "System.Int32" or "System.UInt32" or "System.IntPtr" or "System.UIntPtr"=>4,_ when model.Types.ContainsKey(type)=>Math.Max(1,TotalInstanceSize(type,model)),_=>4};
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
    private static IReadOnlyList<string> InterfaceClosure(string type,CompilationModel model)
    {
        var result=new List<string>();var seen=new HashSet<string>();
        void Add(string current){if(!model.Types.TryGetValue(current,out var tm))return;foreach(var iface in tm.Interfaces??Array.Empty<string>())if(seen.Add(iface)){result.Add(iface);Add(iface);}if(tm.BaseType is { } parent)Add(parent);}
        Add(type);return result;
    }
    private static IReadOnlyList<MethodModel> InterfaceImplementations(string type,string iface,CompilationModel model,HashSet<MethodKey> compiled)
    {
        var methods=model.Methods.Values.Where(m=>m.Key.TypeName==iface&&m.IsVirtual&&!m.Key.Signature.Contains("|contract:",StringComparison.Ordinal)).OrderBy(m=>m.Handle.GetHashCode()).ToArray();
        var result=new List<MethodModel>();
        foreach(var contract in methods)
        {
            MethodModel? implementation=null;var current=type;
            while(model.Types.ContainsKey(current))
            {
                var candidates=model.Methods.Values.Where(m=>m.Key.TypeName==current&&!m.IsAbstract&&compiled.Contains(m.Key)&&m.ParameterCount==contract.ParameterCount);
                implementation=candidates.FirstOrDefault(m=>m.Key.Name==iface+"."+contract.Key.Name)
                    ??candidates.FirstOrDefault(m=>m.Key.Name==contract.Key.Name);
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
        var methods=model.Methods.Values.Where(m=>m.Key.TypeName==target.TypeName&&m.IsVirtual&&!m.Key.Signature.Contains("|contract:",StringComparison.Ordinal)).OrderBy(m=>m.Handle.GetHashCode()).ToList();
        var parameterCount=model.Methods.TryGetValue(target,out var contract)?contract.ParameterCount:0;
        var slot=methods.FindIndex(m=>m.Key.Name==target.Name&&m.ParameterCount==parameterCount);
        if(slot<0&&target.Signature.Contains("|contract:",StringComparison.Ordinal))slot=methods.FindIndex(m=>m.Key.Name==target.Name);
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
    private static bool IsScalarBoxType(string type)=>type is "System.Boolean" or "System.Byte" or "System.SByte" or "System.Char" or "System.Int16" or "System.UInt16" or "System.UInt32" or "System.Int64";
    private static string TypeExpr(string type)=>type switch{"System.String"=>"&DND_TYPE_STRING","System.Object"=>"&DND_TYPE_OBJECT","System.Int32"=>"&DND_TYPE_BOXED_INT32","System.Int64"=>"&DND_TYPE_INT64","System.Boolean"=>"&DND_TYPE_BOOLEAN","System.Byte"=>"&DND_TYPE_BYTE","System.SByte"=>"&DND_TYPE_SBYTE","System.Char"=>"&DND_TYPE_CHAR","System.Int16"=>"&DND_TYPE_INT16","System.UInt16"=>"&DND_TYPE_UINT16","System.UInt32"=>"&DND_TYPE_UINT32","System.Exception"=>"&DND_TYPE_EXCEPTION","System.SystemException"=>"&DND_TYPE_SYSTEM_EXCEPTION","System.InvalidOperationException"=>"&DND_TYPE_INVALID_OPERATION_EXCEPTION","System.ArgumentException"=>"&DND_TYPE_ARGUMENT_EXCEPTION","System.ArgumentNullException"=>"&DND_TYPE_ARGUMENT_NULL_EXCEPTION","System.ArgumentOutOfRangeException"=>"&DND_TYPE_ARGUMENT_OUT_OF_RANGE_EXCEPTION","System.IndexOutOfRangeException"=>"&DND_TYPE_INDEX_OUT_OF_RANGE_EXCEPTION","System.NullReferenceException"=>"&DND_TYPE_NULL_REFERENCE_EXCEPTION","System.InvalidCastException"=>"&DND_TYPE_INVALID_CAST_EXCEPTION","System.NotSupportedException"=>"&DND_TYPE_NOT_SUPPORTED_EXCEPTION","System.OutOfMemoryException"=>"&DND_TYPE_OUT_OF_MEMORY_EXCEPTION",_ when type.StartsWith("System.",StringComparison.Ordinal)=>"NULL",_=>$"&dnd_type_{Id(type)}"};
    private static string LegacySymbol(MethodKey k)=>"dnd_value_"+Id(k.TypeName)+"_"+Id(k.Name);
    private static string StableId(string s){uint h=2166136261;foreach(var ch in s){h^=ch;h*=16777619;}return h.ToString("x8");}
    private static string Parameters(ValueIrMethod m,CompilationModel model)
    {
        var n=m.ParameterCount+(m.HasThis?1:0);if(n==0)return "void";
        model.Methods.TryGetValue(m.Key,out var mm);var abi=mm?.Abi;
        return string.Join(", ",Enumerable.Range(0,n).Select(i=>{
            if(m.HasThis&&i==0)return $"intptr_t a{i}";
            var pi=i-(m.HasThis?1:0);var kind=abi is not null&&pi<abi.Parameters.Count?abi.Parameters[pi]:CilStackKind.NativeInt;
            return $"{AbiCType(kind)} a{i}";
        }));
    }
    private static string ReturnCType(ValueIrMethod m,CompilationModel model)
        =>model.Methods.TryGetValue(m.Key,out var mm)&&mm.Abi?.Return is { } kind?AbiCType(kind):"intptr_t";
    private static string AbiCType(CilStackKind kind)=>kind switch{CilStackKind.I8=>"int64_t",CilStackKind.Float=>"double",_=>"intptr_t"};
    private static string Id(string s)=>new(s.Select(ch=>char.IsLetterOrDigit(ch)?ch:'_').ToArray());
    private static string Escape(string s)=>s.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\n","\\n").Replace("\r","\\r").Replace("\t","\\t");
    private static bool HasRoots(ValueIrMethod m,CompilationModel model)=>Collect(m).Any(v=>v.Kind==IrValueKind.ObjectReference)||m.Locals.Any(l=>l.Kind==IrValueKind.ObjectReference)||m.Locals.Any(l=>(l.ReferenceOffsets?.Count??0)>0)||m.HasThis||(model.Methods.TryGetValue(m.Key,out var mm)&&mm.Abi is { } abi&&abi.Parameters.Any(k=>k==CilStackKind.ObjectReference));
    private static string ValueStorageCType(IrValueKind kind)=>kind==IrValueKind.I8?"int64_t":"intptr_t";
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
            case ValueIrLoadFunction x:yield return x.Result;if(x.Object is { } fo)yield return fo;break;case ValueIrNewDelegate x:yield return x.Result;if(x.Target is { } dt)yield return dt;yield return x.Function;break;case ValueIrDelegateInvoke x:if(x.Result is { } di)yield return di;yield return x.Delegate;foreach(var a in x.Arguments)yield return a;break;case ValueIrCall x:if(x.Result is { } cr)yield return cr;foreach(var a in x.Arguments)yield return a;break;case ValueIrNewObject x:yield return x.Result;foreach(var a in x.Arguments)yield return a;break;case ValueIrNewException x:yield return x.Result;if(x.Message is { } em)yield return em;break;case ValueIrExceptionMessage x:yield return x.Result;yield return x.Exception;break;case ValueIrLoadField x:yield return x.Result;yield return x.Object;break;case ValueIrStoreField x:yield return x.Object;yield return x.Value;break;case ValueIrLoadStaticField x:yield return x.Result;break;case ValueIrStoreStaticField x:yield return x.Value;break;case ValueIrLoadString x:yield return x.Result;break;case ValueIrTypeTest x:yield return x.Result;yield return x.Object;break;case ValueIrStringLength x:yield return x.Result;yield return x.String;break;case ValueIrPrimitiveToString x:yield return x.Result;yield return x.Value;break;case ValueIrStringOperation x:yield return x.Result;foreach(var a in x.Arguments)yield return a;break;case ValueIrBox x:yield return x.Result;yield return x.Value;break;case ValueIrUnboxAny x:yield return x.Result;yield return x.Object;break;case ValueIrNewArray x:yield return x.Result;yield return x.Length;break;case ValueIrArrayElementAddress x:yield return x.Result;yield return x.Array;yield return x.Index;break;case ValueIrArrayLength x:yield return x.Result;yield return x.Array;break;case ValueIrArrayOperation x:if(x.Result is { } ao)yield return ao;foreach(var a in x.Arguments)yield return a;break;case ValueIrLoadElement x:yield return x.Result;yield return x.Array;yield return x.Index;break;case ValueIrStoreElement x:yield return x.Array;yield return x.Index;yield return x.Value;break;case ValueIrConsoleWriteLine x:yield return x.String;break;case ValueIrReadButtonsDown x:yield return x.Result;yield return x.Port;break;case ValueIrPresentDemoFrame x:yield return x.Rotation;break;case ValueIrConstant x:yield return x.Result;break;case ValueIrLoadArgument x:yield return x.Result;break;case ValueIrLoadLocal x:yield return x.Result;break;case ValueIrStoreLocal x:yield return x.Value;break;case ValueIrStoreLocalStruct x:yield return x.SourceAddress;break;case ValueIrNullableInit x:yield return x.Address;yield return x.Value;break;case ValueIrNullableHasValue x:yield return x.Result;yield return x.Address;break;case ValueIrNullableGetValue x:yield return x.Result;yield return x.Address;break;case ValueIrNullableGetValueOrDefault x:yield return x.Result;yield return x.Address;yield return x.DefaultValue;break;case ValueIrNullableEquals x:yield return x.Result;yield return x.Address;yield return x.Other;break;case ValueIrNullableHash x:yield return x.Result;yield return x.Address;break;case ValueIrNewStruct x:yield return x.Result;foreach(var f in x.Fields)yield return f.Value;break;case ValueIrStructStore x:yield return x.Address;yield return x.Value;break;case ValueIrStructLoad x:yield return x.Result;yield return x.Address;break;case ValueIrArraySegmentItem x:yield return x.Result;yield return x.Address;yield return x.Index;break;case ValueIrArraySegmentGetEnumerator x:yield return x.Result;yield return x.Segment;break;case ValueIrEnumeratorMoveNext x:yield return x.Result;yield return x.Address;break;case ValueIrEnumeratorCurrent x:yield return x.Result;yield return x.Address;break;case ValueIrBoxNullable x:yield return x.Result;yield return x.Address;break;case ValueIrAddressOfLocal x:yield return x.Result;break;case ValueIrAddressOfArgument x:yield return x.Result;break;case ValueIrStoreArgument x:yield return x.Value;break;case ValueIrLoadIndirect x:yield return x.Result;yield return x.Address;break;case ValueIrStoreIndirect x:yield return x.Address;yield return x.Value;break;case ValueIrInitObject x:yield return x.Address;break;case ValueIrCopyObject x:yield return x.Destination;yield return x.Source;break;case ValueIrConvert x:yield return x.Result;yield return x.Value;break;case ValueIrBinary x:yield return x.Result;break;case ValueIrPhi x:yield return x.Result;foreach(var v in x.Inputs.Values)yield return v;break;case ValueIrOpaqueStackEffect x:foreach(var v in x.Results)yield return v;break;
        }
        foreach(var b in m.Blocks){foreach(var v in b.EntryStack.Values)yield return v;foreach(var v in b.ExitStack.Values)yield return v;if(b.Terminator is ValueIrBranch br){yield return br.Left;if(br.Right is { } r)yield return r;}else if(b.Terminator is ValueIrSwitch sw)yield return sw.Value;else if(b.Terminator is ValueIrReturn ret&&ret.Value is { } rv)yield return rv;else if(b.Terminator is ValueIrThrow thr&&thr.Exception is { } ex)yield return ex;}
    }
}
