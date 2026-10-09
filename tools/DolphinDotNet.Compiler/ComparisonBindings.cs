using System.Text;
namespace DolphinDotNet.Compiler;
internal static class ComparisonBindings
{
    internal static string Expression(ValueIrDefaultComparison x,CompilationModel model)=>Expression(x.Operation,x.Type,x.Arguments.Select(a=>$"v{a.Id}").ToArray(),model);
    private static string Expression(string op,GenericRepresentation rep,string[] args,CompilationModel model)
    {
        var a=args[0];var z=args.Length>1?args[1]:"0";
        if(rep.TypeName is {} enumName&&model.Types.TryGetValue(enumName,out var enumeration)&&enumeration.BaseType=="System.Enum")return Expression(op,model.Fields[(enumName,"value__")].Representation??new GenericRepresentation(GenericRepresentationKind.PointerSized,rep.Size),args,model);
        if(rep.TypeName=="System.Nullable`1"&&rep.Arguments is { Count:1 } children)
        {
            var child=children[0];string Read(string p)=>child.IsAggregate?$"((intptr_t)((uint8_t*){p}+4))":child.IsReference?$"(*(intptr_t*)((uint8_t*){p}+4))":child.Size==8?$"dnd_read_i64((uint8_t*){p}+4)":child.Size==1?$"(*(uint8_t*)((uint8_t*){p}+4))":child.Size==2?$"dnd_read_u16((uint8_t*){p}+4)":$"dnd_read_i32((uint8_t*){p}+4)";
            var inner=Expression(op,child,args.Select(Read).ToArray(),model);
            return op=="Hash"?$"(dnd_read_i32((void*){a})?{inner}:0)":op=="Equal"?$"(dnd_read_i32((void*){a})==dnd_read_i32((void*){z})&&(!dnd_read_i32((void*){a})||{inner}))":$"(!dnd_read_i32((void*){a})?(dnd_read_i32((void*){z})?-1:0):!dnd_read_i32((void*){z})?1:{inner})";
        }
        if(rep.IsReference)
        {
            if(rep.TypeName=="System.String")return op switch { "Equal"=>$"dnd_string_equals((DndString*){a},(DndString*){z})", "Hash"=>$"dnd_comparison_string_hash((DndString*){a})",_=>$"dnd_comparison_string_order((DndString*){a},(DndString*){z})" };
            return $"dnd_comparison_object_{op.ToLowerInvariant()}((DndObject*){a}"+(op=="Hash"?")":$",(DndObject*){z},{(rep.TypeName=="System.Object"?"false":"true")})");
        }
        if(rep.IsAggregate&&rep.TypeName is {} type&&model.Types.ContainsKey(type))return $"dnd_comparison_{Id(type)}_{op.ToLowerInvariant()}((intptr_t){a}"+(op=="Hash"?")":$",(intptr_t){z})");
        if(rep.TypeName is "System.Single" or "System.Double"){
            var castFloat=rep.TypeName=="System.Single"?"float":"double";
            if(op=="Hash")return $"dnd_comparison_float_hash(({castFloat}){a})";
            if(op=="Equal")return $"({a}=={z}||(isnan({a})&&isnan({z})))";
            return $"({a}<{z}?-1:{a}>{z}?1:{a}=={z}?0:isnan({a})?(isnan({z})?0:-1):1)";
        }
        var cast=rep.TypeName is "System.UInt64"?"uint64_t":rep.TypeName is "System.UInt32"?"uint32_t":rep.TypeName is "System.UInt16" or "System.Char"?"uint16_t":rep.TypeName is "System.Byte" or "System.Boolean"?"uint8_t":rep.TypeName is "System.SByte"?"int8_t":rep.TypeName is "System.Int16"?"int16_t":rep.Size==8?"int64_t":"int32_t";
        if(op=="Hash")return rep.Size==8?$"(int32_t)((uint64_t){a}^((uint64_t){a}>>32))":$"(int32_t)({cast}){a}";
        if(op=="Equal")return $"(({cast}){a}==({cast}){z})";
        return $"(({cast}){a}<({cast}){z}?-1:({cast}){a}>({cast}){z}?1:0)";
    }
    internal static string EmitHelpers(IReadOnlyList<ValueIrMethod> methods,CompilationModel model,DependencyGraph graph)
    {
        if(!methods.SelectMany(m=>m.Blocks).SelectMany(b=>b.Instructions).Any(i=>i is ValueIrDefaultComparison or ValueIrObjectEquals or ValueIrObjectHash))return "";
        var compiled=methods.Select(m=>m.Key).ToHashSet();var b=new StringBuilder();
        b.AppendLine("static inline int32_t dnd_comparison_float_hash(double x) {if(x==0)return 0;if(isnan(x))return 0x7ff80000;uint64_t bits;memcpy(&bits,&x,8);return (int32_t)(bits^(bits>>32));}");
        b.AppendLine("static inline int32_t dnd_comparison_string_hash(DndString *s) { if(!s)return 0;uint32_t h=2166136261u;for(uint32_t i=0;i<s->length;i++)h=(h^s->chars[i])*16777619u;return (int32_t)h; }");
        b.AppendLine("static inline int32_t dnd_comparison_string_order(DndString *a,DndString *z) { if(a==z){return 0;}if(!a){return -1;}if(!z){return 1;}uint32_t n=a->length<z->length?a->length:z->length;for(uint32_t i=0;i<n;i++)if(a->chars[i]!=z->chars[i])return a->chars[i]<z->chars[i]?-1:1;return a->length<z->length?-1:a->length>z->length?1:0; }");
        foreach(var op in new[]{"equal","hash","compare"})b.AppendLine($"static inline int32_t dnd_comparison_object_{op}(DndObject *a{(op=="hash"?"":",DndObject *z,bool typed")});");
        var types=model.Types.Keys.Where(t=>model.Types.TryGetValue(t,out var tm)&&!tm.IsInterface).ToArray();
        foreach(var type in types.Where(t=>model.Types[t].IsValueType))foreach(var op in new[]{"equal","boxequal","hash","compare"})b.AppendLine($"static inline int32_t dnd_comparison_{Id(type)}_{op}(intptr_t a{(op=="hash"?"":",intptr_t z")});");
        MethodModel? Find(string type,string op,bool typed=true)=>model.Methods.Values.Where(m=>m.Key.TypeName==type&&compiled.Contains(m.Key)&&!m.IsStatic&&m.ParameterCount==(op=="Hash"?0:1)&&(m.ExplicitContracts?.Any(c=>c.Method==(op=="Equal"?"Equals":"CompareTo"))==true||m.Key.Name==(op=="Hash"?"GetHashCode":op=="Equal"?"Equals":"CompareTo"))).Where(m=>op=="Hash"||typed||m.Handle.IsNil||model.Assemblies[m.AssemblyName].Metadata.GetMethodDefinition(m.Handle).DecodeSignature(new SignatureAbi(model),new SignatureAbi.Context(m.TypeArguments??Array.Empty<GenericRepresentation>(),m.MethodArguments??Array.Empty<GenericRepresentation>())).ParameterTypes[0].Name=="System.Object").OrderByDescending(m=>m.ExplicitContracts?.Count??0).FirstOrDefault();
        foreach(var type in types.Where(t=>model.Types[t].IsValueType))foreach(var op in new[]{"Equal","BoxEqual","Hash","Compare"})
        {
            b.AppendLine($"static inline int32_t dnd_comparison_{Id(type)}_{op.ToLowerInvariant()}(intptr_t a{(op=="Hash"?"":",intptr_t z")}) {{ (void)a;");
            var method=Find(type,op=="BoxEqual"?"Equal":op,op!="BoxEqual");
            if(method is not null)b.AppendLine($"return (int32_t){ValueCBackend.Symbol(method.Key)}(a{(op=="Hash"?"":",z")});");
            else if(op=="Compare")b.AppendLine("(void)a;(void)z;dnd_exception_throw(DND_EXCEPTION_ARGUMENT,\"Type does not implement comparison.\");return 0;");
            else
            {
                var fields=model.Fields.Values.Where(f=>f.DeclaringType==type&&!f.IsStatic).OrderBy(f=>f.Offset).ToArray();
                b.AppendLine(op=="Hash"?"uint32_t h=0;":"(void)a;(void)z;");
                foreach(var f in fields)
                {
                    var r=f.Representation??new GenericRepresentation(f.IsReference?GenericRepresentationKind.PointerSized:GenericRepresentationKind.ValueType,f.Size,f.IsReference?new[]{0}:null,f.IsReference?"System.Object":f.Size==8?"System.Int64":"System.Int32");
                    string Read(string v)=>r.IsAggregate&&r.TypeName is {} n&&model.Types.ContainsKey(n)?$"({v}+{f.Offset})":r.TypeName is "System.Single"?$"(*(float*)({v}+{f.Offset}))":r.TypeName is "System.Double"?$"(*(double*)({v}+{f.Offset}))":f.IsReference?$"(*(intptr_t*)({v}+{f.Offset}))":f.Size==8?$"dnd_read_i64((void*)({v}+{f.Offset}))":f.Size==1?$"(*(uint8_t*)({v}+{f.Offset}))":f.Size==2?$"dnd_read_u16((void*)({v}+{f.Offset}))":$"dnd_read_i32((void*)({v}+{f.Offset}))";
                    var e=Expression(op=="BoxEqual"?"Equal":op,r,op=="Hash"?new[]{Read("a")}:new[]{Read("a"),Read("z")},model);
                    b.AppendLine(op=="Hash"?$"h=(h*31u)+(uint32_t){e};":$"if(!{e})return 0;");
                }
                b.AppendLine(op=="Hash"?"return (int32_t)h;":"return 1;");
            }
            b.AppendLine("}");
        }
        foreach(var op in new[]{"Equal","Hash","Compare"})
        {
            b.AppendLine($"static inline int32_t dnd_comparison_object_{op.ToLowerInvariant()}(DndObject *a{(op=="Hash"?"":",DndObject *z,bool typed")}) {{ (void)a;");
            if(op!="Hash")b.AppendLine("(void)typed;");
            b.AppendLine(op=="Hash"?"if(!a)return 0;":op=="Equal"?"if(a==z&&(!typed||!a)){return 1;}if(!a||!z){return 0;}":"if(a==z){return 0;}if(!a){return -1;}if(!z){return 1;}");
            b.AppendLine(op=="Hash"?"if(a->type==&DND_TYPE_STRING)return dnd_comparison_string_hash((DndString*)a);":$"if(a->type==&DND_TYPE_STRING&&z->type==&DND_TYPE_STRING)return {(op=="Equal"?"dnd_string_equals":"dnd_comparison_string_order")}((DndString*)a,(DndString*)z);");
            foreach(var type in new[]{"System.Int32","System.Int64","System.UInt64","System.Single","System.Double","System.Boolean","System.Byte","System.SByte","System.Char","System.Int16","System.UInt16","System.UInt32"})
            {
                var size=type is "System.Int64" or "System.UInt64" or "System.Double"?8:type is "System.Byte" or "System.SByte" or "System.Boolean"?1:type is "System.Char" or "System.Int16" or "System.UInt16"?2:4;
                var rep=new GenericRepresentation(size==8?GenericRepresentationKind.ValueType:GenericRepresentationKind.PointerSized,size,null,type);
                string Read(string v)=>type is "System.Single" or "System.Double"?$"dnd_float_value(dnd_unbox_scalar({v},{ValueCBackend.TypeExpr(type)},{size}u),{(type=="System.Single"?"true":"false")})":$"dnd_unbox_scalar({v},{ValueCBackend.TypeExpr(type)},{size}u)";
                b.AppendLine($"if(a->type=={ValueCBackend.TypeExpr(type)}{(op=="Hash"?"":$"&&z->type=={ValueCBackend.TypeExpr(type)}")})return {Expression(op,rep,op=="Hash"?new[]{Read("a")}:new[]{Read("a"),Read("z")},model)};");
            }
            foreach(var type in types.Where(t=>model.Types[t].IsValueType&&graph.Types.Contains(t)))
                b.AppendLine($"if(a->type=={ValueCBackend.TypeExpr(type)}{(op=="Hash"?"":$"&&z->type=={ValueCBackend.TypeExpr(type)}")})return dnd_comparison_{Id(type)}_{(op=="Equal"?"boxequal":op.ToLowerInvariant())}((intptr_t)((uint8_t*)a+sizeof(DndObject)){(op=="Hash"?"":",(intptr_t)((uint8_t*)z+sizeof(DndObject))")});");
            foreach(var type in types.Where(t=>!model.Types[t].IsValueType&&graph.Types.Contains(t)))
            {
                foreach(var useTyped in op=="Hash"?new[]{true}:new[]{true,false}){
                    var current=type;MethodModel? method=null;
                    while(model.Types.TryGetValue(current,out var currentType)){method=Find(current,op,useTyped);if(method!=null)break;current=currentType.BaseType??"";}
                    if(method is null)continue;
                    b.AppendLine($"if({(op=="Hash"?"":useTyped?"typed&&":"!typed&&")}a->type=={ValueCBackend.TypeExpr(type)}{(op=="Hash"||!useTyped?"":$"&&dnd_type_is_assignable_from({ValueCBackend.TypeExpr(method.Key.TypeName)},z->type)")})return (int32_t){ValueCBackend.Symbol(method.Key)}((intptr_t)a{(op=="Hash"?"":",(intptr_t)z")});");
                }
            }
            b.AppendLine(op=="Hash"?"return (int32_t)((uintptr_t)a>>3);":op=="Equal"?"return a==z;":"dnd_exception_throw(DND_EXCEPTION_ARGUMENT,\"Type does not implement comparison.\");return 0;");b.AppendLine("}");
        }
        return b.ToString();
    }
    private static string Id(string s)=>new(s.Select(c=>char.IsLetterOrDigit(c)?c:'_').ToArray());
}
