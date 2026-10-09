using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace DolphinDotNet.Compiler;

internal sealed record AotCompilation(CompilationModel Model,DependencyGraph Graph,List<IrMethod> Methods,List<ValueIrMethod> ValueMethods);

internal static class AotCompiler
{
    public static AotCompilation Compile(string path)
    {
        var model=new CompilationModel();
        try
        {
            var root=LoadAssembly(model,Path.GetFullPath(path));
            LoadDolphinDependencies(model,root);
            var corePath=Path.Combine(Path.GetDirectoryName(root.Path)!,"DolphinDotNet.Core.dll");
            if(File.Exists(corePath)&&!model.Assemblies.ContainsKey("DolphinDotNet.Core"))LoadAssembly(model,corePath);
            MetadataLoader.RefreshLayouts(model);
            foreach(var mm in model.Methods.Values.ToArray())
                model.Methods[mm.Key]=mm with { Abi=SignatureAbi.Decode(model.Assemblies[mm.AssemblyName].Metadata,mm,model,Array.Empty<GenericRepresentation>(),Array.Empty<GenericRepresentation>()) };
            EnsureEnumerationContracts(model); // collection contracts are synthesized here too
            var cor=root.PE.PEHeaders.CorHeader??throw new InvalidDataException("Missing CLI header.");
            if(cor.EntryPointTokenOrRelativeVirtualAddress==0)throw new InvalidDataException("Assembly has no managed entry point.");
            var entry=MetadataTokens.EntityHandle(cor.EntryPointTokenOrRelativeVirtualAddress);
            if(entry.Kind!=HandleKind.MethodDefinition)throw new NotSupportedException("Only MethodDef entry points are supported.");
            var def=root.Metadata.GetMethodDefinition((MethodDefinitionHandle)entry);var td=root.Metadata.GetTypeDefinition(def.GetDeclaringType());
            var entryType=Full(root.Metadata.GetString(td.Namespace),root.Metadata.GetString(td.Name));
            var entrySignature=Convert.ToHexString(root.Metadata.GetBlobBytes(def.Signature));
            var entryKey=new MethodKey(entryType,root.Metadata.GetString(def.Name),root.Name,entrySignature);
            if(!model.Methods.TryGetValue(entryKey,out var entryMethod))throw new InvalidDataException("Entry point missing from model.");

            var graph=new DependencyGraph();var output=new List<IrMethod>();var valueOutput=new List<ValueIrMethod>();var queue=new Queue<MethodModel>();var queued=new HashSet<MethodKey>();
            queue.Enqueue(entryMethod);queued.Add(entryMethod.Key);
            while(queue.Count>0)
            {
                var method=queue.Dequeue();
                var assembly=model.Assemblies[method.AssemblyName];
                var body=assembly.PE.GetMethodBody(assembly.Metadata.GetMethodDefinition(method.Handle).RelativeVirtualAddress);
                var ilBytes=body.GetILBytes()??throw new InvalidDataException($"{method.Key} has no IL body.");
                var cil=CilDecoder.Decode(ilBytes);
                var cfg=CilControlFlowGraph.Build(cil,body.ExceptionRegions);
                CilStackAnalysis stackAnalysis;try{stackAnalysis=CilStackAnalyzer.Analyze(cfg,i=>ResolveCallEffect(assembly.Metadata,model,i,method),method.ReturnsValue,body.ExceptionRegions);}catch(Exception ex){if(Environment.GetEnvironmentVariable("DND_DEBUG_IL")=="1")foreach(var i in cil)Console.Error.WriteLine($"{i.Offset:x4} {i.OpCode:x4} {i.Operand} {ResolveCallEffect(assembly.Metadata,model,i,method)}");throw new InvalidDataException($"Stack analysis failed for {method.Key}: {ex.Message}",ex);}
                var valueIr=ValueIrImporter.Import(method,cfg,stackAnalysis,ReadLocalStorage(assembly,method,model),i=>ResolveCall(assembly.Metadata,model,i,method),i=>ResolveCallEffect(assembly.Metadata,model,i,method),i=>IsIgnoredCall(assembly.Metadata,i),i=>ResolveIntrinsic(assembly.Metadata,i),i=>NullableValueSize(assembly.Metadata,model,i),i=>GenericArguments(assembly.Metadata,model,i,method),i=>ResolveString(assembly.Metadata,i),i=>ResolveField(assembly.Metadata,model,i,method),i=>ResolveTypeForContext(assembly.Metadata,model,method,i),i=>ResolveGenericTypeParameter(assembly.Metadata,model,method,i),t=>model.Types.TryGetValue(t,out var vm)&&vm.IsValueType?(vm.InstanceSize,model.Fields.Values.Where(f=>f.DeclaringType==t&&!f.IsStatic&&f.IsReference).Select(f=>f.Offset).ToArray()):(0,null),(constraint,name)=>ResolveConstrained(assembly.Metadata,model,method,constraint,name),i=>ResolveArrayEnumerator(assembly.Metadata,model,method,i),i=>ReadFieldData(assembly,model,i),body.ExceptionRegions)
                    with { ExceptionRegions = ReadExceptionRegions(assembly.Metadata,body.ExceptionRegions) };
                ValueIrVerifier.Verify(valueIr);valueOutput.Add(valueIr);
                Discover(valueIr,model,graph);
                DiscoverVirtuals(model,graph);
                DiscoverTypeInitializers(model,graph);
                try { output.Add(IlImporter.Import(assembly.PE,model,method,new DependencyGraph())); }
                catch(NotSupportedException) { /* Legacy backend is a regression oracle, not a production dependency. */ }
                foreach(var key in graph.Methods)
                    if(queued.Add(key)&&model.Methods.TryGetValue(key,out var reachable)&&model.Assemblies.TryGetValue(reachable.AssemblyName,out var reachableAssembly)&&reachableAssembly.Metadata.GetMethodDefinition(reachable.Handle).RelativeVirtualAddress!=0)queue.Enqueue(reachable);
            }
            return new AotCompilation(model,graph,output,valueOutput);
        }
        catch { model.Dispose(); throw; }
    }

    private static IReadOnlyList<ValueIrExceptionRegion> ReadExceptionRegions(MetadataReader md,ImmutableArray<ExceptionRegion> regions)
    {
        return regions.Select(region=>new ValueIrExceptionRegion(
            region.Kind switch {
                ExceptionRegionKind.Catch=>ValueIrExceptionRegionKind.Catch,
                ExceptionRegionKind.Finally=>ValueIrExceptionRegionKind.Finally,
                ExceptionRegionKind.Fault=>ValueIrExceptionRegionKind.Fault,
                ExceptionRegionKind.Filter=>ValueIrExceptionRegionKind.Filter,
                _=>throw new NotSupportedException($"Unsupported EH region {region.Kind}.")
            },
            region.TryOffset,region.TryLength,region.HandlerOffset,region.HandlerLength,region.FilterOffset,
            region.Kind==ExceptionRegionKind.Catch&&!region.CatchType.IsNil?MetadataLoader.ResolveTypeName(md,region.CatchType):null)).ToArray();
    }

    private static void AddTypeClosure(string type,CompilationModel model,DependencyGraph graph)
    {
        if(!model.Types.TryGetValue(type,out var tm))return;
        if(tm.BaseType is { } parent&&model.Types.ContainsKey(parent)){graph.AddType(parent);AddTypeClosure(parent,model,graph);}
        foreach(var iface in tm.Interfaces??Array.Empty<string>())if(model.Types.ContainsKey(iface)){graph.AddType(iface);AddTypeClosure(iface,model,graph);}
    }

    private static void DiscoverTypeInitializers(CompilationModel model,DependencyGraph graph)
    {
        foreach(var method in model.Methods.Values)
            if(method.Key.Name==".cctor"&&graph.Types.Contains(method.Key.TypeName))graph.AddMethod(method.Key);
    }

    private static void DiscoverVirtuals(CompilationModel model,DependencyGraph graph)
    {
        foreach(var type in graph.Types.ToArray())
        {
            var tm=model.Types.GetValueOrDefault(type);var original=tm?.GenericDefinition??type;
            foreach(var definition in model.Methods.Values.Where(m=>m.IsVirtual&&m.Key.TypeName==original&&!m.Key.Signature.Contains("|g:")).ToArray())
            {
                if(!model.Assemblies.TryGetValue(definition.AssemblyName,out var assembly)||assembly.Metadata.GetMethodDefinition(definition.Handle).RelativeVirtualAddress==0)continue;
                var method=tm?.TypeArguments is { } args?IlImporter.Specialize(assembly.Metadata,model,definition,args,Array.Empty<GenericRepresentation>()):definition;
                graph.AddMethod(method.Key);
            }
        }
    }

    private static void Discover(ValueIrMethod method,CompilationModel model,DependencyGraph graph)
    {
        graph.AddMethod(method.Key);graph.AddType(method.Key.TypeName);AddTypeClosure(method.Key.TypeName,model,graph);
        foreach(var instruction in method.Blocks.SelectMany(b=>b.Instructions))
        {
            switch(instruction)
            {
                case ValueIrArrayOperation { InterfaceTarget: { } target }: graph.AddType(target.TypeName);break;
                case ValueIrLoadFunction fn: graph.AddMethod(fn.Target);graph.AddType(fn.Target.TypeName);break;
                case ValueIrCall call: if(call.ArrayFallback is {} af){graph.AddMethod(af);graph.AddType(af.TypeName);}if(!model.Methods.TryGetValue(call.Target,out var called)||!called.IsAbstract)graph.AddMethod(call.Target);graph.AddType(call.Target.TypeName);AddTypeClosure(call.Target.TypeName,model,graph);break;
                case ValueIrDefaultComparison comparison: if(comparison.Type.TypeName is {} tn&&model.Types.ContainsKey(tn)){graph.AddType(tn);AddTypeClosure(tn,model,graph);}break;
                case ValueIrNewDelegate: break;
                case ValueIrDelegateInvoke: break;
                case ValueIrBox boxed:if(model.Types.ContainsKey(boxed.TypeName)){graph.AddType(boxed.TypeName);AddTypeClosure(boxed.TypeName,model,graph);}break;
                case ValueIrValueConstructor value:graph.AddMethod(value.Constructor);graph.AddType(value.TypeName);break;
                case ValueIrNewObject created: graph.AddMethod(created.Constructor);graph.AddType(created.TypeName);break;
                case ValueIrLoadField field: graph.AddType(field.TypeName);break;
                case ValueIrStoreField field: graph.AddType(field.TypeName);break;
                case ValueIrLoadStaticField field: graph.AddType(field.TypeName);break;
                case ValueIrStoreStaticField field: graph.AddType(field.TypeName);break;
                case ValueIrTypeTest test: graph.AddType(test.TypeName);break;
                case ValueIrInitObject init: graph.AddType(init.TypeName);break;
                case ValueIrCopyObject copy: graph.AddType(copy.TypeName);break;
                case ValueIrNewArray array: if(!array.ElementType.StartsWith("System.",StringComparison.Ordinal))graph.AddType(array.ElementType);break;
            }
        }
    }

    private static IReadOnlyList<LocalStorage> ReadLocalStorage(AssemblyModel assembly,MethodModel method,CompilationModel model)
    {
        var def=assembly.Metadata.GetMethodDefinition(method.Handle);var body=assembly.PE.GetMethodBody(def.RelativeVirtualAddress);
        if(body.LocalSignature.IsNil)return Array.Empty<LocalStorage>();
        var types=assembly.Metadata.GetStandaloneSignature(body.LocalSignature).DecodeLocalSignature(new SignatureAbi(model),new SignatureAbi.Context(method.TypeArguments??Array.Empty<GenericRepresentation>(),method.MethodArguments??Array.Empty<GenericRepresentation>()));
        return types.Select(t=>new LocalStorage(t.Size,t.Kind??CilStackKind.NativeInt,t.References)).ToArray();
    }

    private static byte[] ReadFieldData(AssemblyModel assembly,CompilationModel model,CilInstruction instruction)
    {
        if(instruction.Operand is not CilMetadataToken { Token:var raw }||MetadataTokens.EntityHandle(raw).Kind!=HandleKind.FieldDefinition)throw new NotSupportedException("Only static field data tokens are supported.");
        var field=assembly.Metadata.GetFieldDefinition((FieldDefinitionHandle)MetadataTokens.EntityHandle(raw));var rva=field.GetRelativeVirtualAddress();
        var signature=field.DecodeSignature(new SignatureAbi(model),new SignatureAbi.Context(Array.Empty<GenericRepresentation>(),Array.Empty<GenericRepresentation>()));
        if(rva==0||signature.Size<=0)throw new NotSupportedException("Field token has no fixed RVA data.");
        return assembly.PE.GetSectionData(rva).GetContent(0,signature.Size).ToArray();
    }
    private static MethodKey? ResolveArrayEnumerator(MetadataReader md,CompilationModel model,MethodModel context,CilInstruction call)
    {
        var method=model.Methods.Values.FirstOrDefault(m=>m.Key.TypeName=="Dolphin.Collections.ArrayEnumerable`1"&&m.Key.Name=="Enumerate");if(method is null)return null;
        return IlImporter.Specialize(md,model,method,GenericArguments(md,model,call,context),Array.Empty<GenericRepresentation>()).Key;
    }
    private static MethodModel? ResolveConstrained(MetadataReader md,CompilationModel model,MethodModel context,CilInstruction constraint,string name)
    {
        if(constraint.Operand is not CilMetadataToken { Token:var raw })return null;
        var h=MetadataTokens.EntityHandle(raw);var type=MetadataLoader.ResolveTypeName(md,h);
        if(type is null)type=ResolveGenericTypeParameter(md,model,context,constraint)?.TypeName;
        if(type is null||!model.Types.TryGetValue(type,out var tm)||!tm.IsValueType)return null;
        var original=tm.GenericDefinition??type;
        var definition=model.Methods.Values.FirstOrDefault(m=>m.Key.TypeName==original&&(m.Key.Name==name||m.ExplicitContracts?.Any(c=>c.Method==name)==true)&&!m.Key.Signature.Contains("|g:",StringComparison.Ordinal));
        if(definition is null)return null;
        var arguments=tm.TypeArguments??(h.Kind==HandleKind.TypeSpecification?GenericSharing.ReadTypeArguments(md,(TypeSpecificationHandle)h,model,context):Array.Empty<GenericRepresentation>());
        return arguments.Count>0?IlImporter.Specialize(md,model,definition,arguments,Array.Empty<GenericRepresentation>()):definition;
    }
    private static MethodModel? ResolveCall(MetadataReader md,CompilationModel model,CilInstruction i,MethodModel? context=null)
    {
        if(i.OpCode is not (0x28 or 0x6f or 0x73 or 0xfe06 or 0xfe07)||i.Operand is not CilMetadataToken { Token: var raw })return null;
        try{return IlImporter.ResolveMethod(md,model,MetadataTokens.EntityHandle(raw),context);}
        catch(NotSupportedException ex){if(ResolveIntrinsic(md,i)==IntrinsicKind.None)throw new NotSupportedException($"Call at IL_{i.Offset:x4}: {ex.Message}",ex);return null;}
    }

    private static string? ResolveTypeForContext(MetadataReader md,CompilationModel model,MethodModel context,CilInstruction instruction)
    {
        var name=ResolveType(md,instruction);
        if(name is null||name is "System.Nullable`1" or "System.ArraySegment`1" or "System.ArraySegment`1+Enumerator" or "System.Collections.Generic.KeyValuePair`2"||!model.Types.TryGetValue(name,out var tm)||tm.IsInterface)return name;
        if(instruction.Operand is not CilMetadataToken { Token:var raw })return name;
        var h=MetadataTokens.EntityHandle(raw);if(h.Kind!=HandleKind.TypeSpecification)return name;
        var args=GenericSharing.ReadTypeArguments(md,(TypeSpecificationHandle)h,model,context);if(args.Count==0)return name;
        var definition=model.Methods.Values.FirstOrDefault(m=>m.Key.TypeName==name&&!m.Handle.IsNil);if(definition is null)return name;
        return IlImporter.Specialize(md,model,definition,args,Array.Empty<GenericRepresentation>()).Key.TypeName;
    }
    private static string? ResolveType(MetadataReader md,CilInstruction i)
    {
        if(i.Operand is not CilMetadataToken { Token: var raw })return null;
        try {
            var handle=MetadataTokens.EntityHandle(raw);
            if(handle.Kind==HandleKind.MethodSpecification)handle=md.GetMethodSpecification((MethodSpecificationHandle)handle).Method;
            if(handle.Kind==HandleKind.MemberReference)return MetadataLoader.ResolveTypeName(md,md.GetMemberReference((MemberReferenceHandle)handle).Parent);
            if(handle.Kind==HandleKind.MethodDefinition)return MetadataLoader.ResolveTypeName(md,md.GetMethodDefinition((MethodDefinitionHandle)handle).GetDeclaringType());
            return MetadataLoader.ResolveTypeName(md,handle);
        } catch{return null;}
    }

    private static GenericRepresentation? ResolveGenericTypeParameter(MetadataReader md,CompilationModel model,MethodModel method,CilInstruction i)
    {
        if(i.Operand is not CilMetadataToken { Token: var raw })return null;
        var handle=MetadataTokens.EntityHandle(raw);
        if(handle.Kind!=HandleKind.TypeSpecification)return null;
        var reader=md.GetBlobReader(md.GetTypeSpecification((TypeSpecificationHandle)handle).Signature);
        var code=reader.ReadSignatureTypeCode();
        if(code is not (SignatureTypeCode.GenericTypeParameter or SignatureTypeCode.GenericMethodParameter))return null;
        var index=reader.ReadCompressedInteger();
        var args=code==SignatureTypeCode.GenericTypeParameter?method.TypeArguments:method.MethodArguments;
        return args is not null&&index<args.Count?args[index]:new GenericRepresentation(GenericRepresentationKind.PointerSized,4);
    }

    private static FieldModel? ResolveField(MetadataReader md,CompilationModel model,CilInstruction i,MethodModel? context=null)
    {
        if(i.OpCode is not (0x7b or 0x7c or 0x7d or 0x7e or 0x80)||i.Operand is not CilMetadataToken { Token: var raw })return null;
        try
        {
            var handle=MetadataTokens.EntityHandle(raw);var field=IlImporter.ResolveField(md,model,handle);
            if(handle.Kind==HandleKind.MemberReference&&md.GetMemberReference((MemberReferenceHandle)handle).Parent is { Kind:HandleKind.TypeSpecification } parent)
            {
                var args=GenericSharing.ReadTypeArguments(md,(TypeSpecificationHandle)parent,model,context);
                var closed=field.DeclaringType+GenericSharing.SpecializationSuffix(args,Array.Empty<GenericRepresentation>());
                if(model.Fields.TryGetValue((closed,field.Name),out var specialized))return specialized;
            }
            return context is not null&&model.Types[context.Key.TypeName].GenericDefinition==field.DeclaringType?model.Fields[(context.Key.TypeName,field.Name)]:field;
        }catch(NotSupportedException ex){if(ResolveIntrinsic(md,i)==IntrinsicKind.None)throw new NotSupportedException($"Call at IL_{i.Offset:x4}: {ex.Message}",ex);return null;}
    }

    private static int NullableValueSize(MetadataReader md,CompilationModel model,CilInstruction i)
    {
        if(i.Operand is not CilMetadataToken { Token: var raw })return 4;
        var handle=MetadataTokens.EntityHandle(raw);
        if(handle.Kind==HandleKind.TypeSpecification)
        {
            var typeArgs=GenericSharing.ReadTypeArguments(md,(TypeSpecificationHandle)handle,model);
            return typeArgs.Count==1?Math.Max(1,typeArgs[0].Size):4;
        }
        if(handle.Kind==HandleKind.MethodSpecification)handle=md.GetMethodSpecification((MethodSpecificationHandle)handle).Method;
        if(handle.Kind!=HandleKind.MemberReference)return 4;
        var member=md.GetMemberReference((MemberReferenceHandle)handle);
        if(member.Parent.Kind!=HandleKind.TypeSpecification)return 4;
        var args=GenericSharing.ReadTypeArguments(md,(TypeSpecificationHandle)member.Parent,model);
        return args.Count==1?Math.Max(1,args[0].Size):4;
    }

    private static IReadOnlyList<GenericRepresentation> GenericArguments(MetadataReader md,CompilationModel model,CilInstruction i,MethodModel? context=null)
    {
        if(i.Operand is not CilMetadataToken { Token: var raw })return Array.Empty<GenericRepresentation>();
        var handle=MetadataTokens.EntityHandle(raw);
        if(handle.Kind==HandleKind.MethodSpecification)return GenericSharing.ReadMethodArguments(md,(MethodSpecificationHandle)handle,model,context);
        if(handle.Kind!=HandleKind.MemberReference)return Array.Empty<GenericRepresentation>();var member=md.GetMemberReference((MemberReferenceHandle)handle);
        return member.Parent.Kind==HandleKind.TypeSpecification?GenericSharing.ReadTypeArguments(md,(TypeSpecificationHandle)member.Parent,model,context):Array.Empty<GenericRepresentation>();
    }

    private static IntrinsicKind ResolveIntrinsic(MetadataReader md,CilInstruction i)
    {
        if(i.OpCode is not (0x28 or 0x6f or 0x73)||i.Operand is not CilMetadataToken { Token: var raw })return IntrinsicKind.None;
        return IntrinsicRegistry.Classify(md,MetadataTokens.EntityHandle(raw));
    }

    private static string? ResolveString(MetadataReader md,CilInstruction i)
    {
        if(i.OpCode!=0x72||i.Operand is not CilMetadataToken { Token: var raw })return null;
        return md.GetUserString(MetadataTokens.UserStringHandle(raw&0x00ffffff));
    }

    private static bool IsIgnoredCall(MetadataReader md,CilInstruction i)
    {
        if(i.OpCode is not (0x28 or 0x6f)||i.Operand is not CilMetadataToken { Token: var raw })return false;
        return IlImporter.TryIgnoreObjectCtor(md,MetadataTokens.EntityHandle(raw));
    }

    private static CilCallStackEffect? ResolveCallEffect(MetadataReader md,CompilationModel model,CilInstruction i,MethodModel? context=null)
    {
        if(i.OpCode is not (0x28 or 0x6f or 0x73)||i.Operand is not CilMetadataToken { Token: var raw })return null;
        var arrayIntrinsic=ResolveIntrinsic(md,i);
        if(arrayIntrinsic==IntrinsicKind.StringFromChars){var member=md.GetMemberReference((MemberReferenceHandle)MetadataTokens.EntityHandle(raw));var reader=md.GetBlobReader(member.Signature);reader.ReadSignatureHeader();return new CilCallStackEffect(reader.ReadCompressedInteger(),CilStackKind.ObjectReference);}
        if(arrayIntrinsic==IntrinsicKind.InitializeArray)return new CilCallStackEffect(2,null);
        if(arrayIntrinsic is IntrinsicKind.ArrayInterfaceCount or IntrinsicKind.ArrayInterfaceIsReadOnly) return new CilCallStackEffect(1,CilStackKind.I4);
        if(arrayIntrinsic is IntrinsicKind.ArrayInterfaceItemGet){var a=GenericArguments(md,model,i,context);var rep=a.Count>0?a[0]:new GenericRepresentation(GenericRepresentationKind.PointerSized,4);return new CilCallStackEffect(2,rep.ContainsReferences?CilStackKind.ObjectReference:rep.Size==8?CilStackKind.I8:CilStackKind.I4);}
        if(arrayIntrinsic==IntrinsicKind.ArrayInterfaceItemSet)return new CilCallStackEffect(3,null);
        if(arrayIntrinsic is IntrinsicKind.ArrayInterfaceContains or IntrinsicKind.ArrayInterfaceIndexOf)return new CilCallStackEffect(2,CilStackKind.I4);
        var resolved=ResolveCall(md,model,i,context);
        if(resolved?.Abi is { } abi)
        {
            var pop=resolved.ParameterCount+(resolved.IsStatic||i.OpCode==0x73?0:1);
            return new CilCallStackEffect(pop,i.OpCode==0x73?CilStackKind.ObjectReference:abi.Return);
        }
        var intrinsic=ResolveIntrinsic(md,i);
        if(intrinsic is IntrinsicKind.NullableConstructor or IntrinsicKind.NullableHasValue or IntrinsicKind.NullableValue or IntrinsicKind.NullableGetValueOrDefault or IntrinsicKind.NullableGetValueOrDefaultValue or IntrinsicKind.NullableEquals or IntrinsicKind.NullableGetHashCode)
        {
            var size=NullableValueSize(md,model,i);
            return intrinsic switch
            {
                IntrinsicKind.NullableConstructor=>i.OpCode==0x73?new CilCallStackEffect(1,CilStackKind.ManagedPointer):new CilCallStackEffect(2,null),
                IntrinsicKind.NullableHasValue=>new CilCallStackEffect(1,CilStackKind.I4),
                IntrinsicKind.NullableGetValueOrDefaultValue=>new CilCallStackEffect(2,size==8?CilStackKind.I8:CilStackKind.I4),
                IntrinsicKind.NullableEquals=>new CilCallStackEffect(2,CilStackKind.I4),
                IntrinsicKind.NullableGetHashCode=>new CilCallStackEffect(1,CilStackKind.I4),
                _=>new CilCallStackEffect(1,size==8?CilStackKind.I8:CilStackKind.I4)
            };
        }
        if(intrinsic==IntrinsicKind.PrimitiveToString)return new CilCallStackEffect(1,CilStackKind.ObjectReference);
        if(intrinsic==IntrinsicKind.ObjectGetHashCode)return new CilCallStackEffect(1,CilStackKind.I4);
        if(intrinsic is IntrinsicKind.ObjectEquals or IntrinsicKind.ObjectStaticEquals)return new CilCallStackEffect(2,CilStackKind.I4);
        if(intrinsic is IntrinsicKind.KeyValuePairConstructor)return i.OpCode==0x73?new CilCallStackEffect(2,CilStackKind.ManagedPointer):new CilCallStackEffect(3,null);
        if(intrinsic is IntrinsicKind.KeyValuePairKey or IntrinsicKind.KeyValuePairValue){var a=GenericArguments(md,model,i,context);var index=intrinsic==IntrinsicKind.KeyValuePairKey?0:1;var rep=a.Count>index?a[index]:new GenericRepresentation(GenericRepresentationKind.PointerSized,4);return new CilCallStackEffect(1,rep.ContainsReferences?CilStackKind.ObjectReference:rep.Size==8?CilStackKind.I8:CilStackKind.I4);}
        if(intrinsic==IntrinsicKind.ArraySegmentConstructor)return i.OpCode==0x73?new CilCallStackEffect(3,CilStackKind.ManagedPointer):new CilCallStackEffect(4,null);
        if(intrinsic==IntrinsicKind.ArraySegmentArray)return new CilCallStackEffect(1,CilStackKind.ObjectReference);
        if(intrinsic is IntrinsicKind.ArraySegmentOffset or IntrinsicKind.ArraySegmentCount)return new CilCallStackEffect(1,CilStackKind.I4);
        if(intrinsic==IntrinsicKind.ArraySegmentItem){var a=GenericArguments(md,model,i,context);var rep=a.Count>0?a[0]:new GenericRepresentation(GenericRepresentationKind.PointerSized,4);return new CilCallStackEffect(2,rep.ContainsReferences?CilStackKind.ObjectReference:rep.Size==8?CilStackKind.I8:CilStackKind.I4);}
        if(intrinsic==IntrinsicKind.ArraySegmentGetEnumerator)return new CilCallStackEffect(1,CilStackKind.ManagedPointer);
        if(intrinsic==IntrinsicKind.ArraySegmentEnumeratorMoveNext)return new CilCallStackEffect(1,CilStackKind.I4);
        if(intrinsic==IntrinsicKind.ArraySegmentEnumeratorCurrent){var a=GenericArguments(md,model,i,context);var rep=a.Count>0?a[0]:new GenericRepresentation(GenericRepresentationKind.PointerSized,4);return new CilCallStackEffect(1,rep.ContainsReferences?CilStackKind.ObjectReference:rep.Size==8?CilStackKind.I8:CilStackKind.I4);}
        if(intrinsic==IntrinsicKind.ArraySegmentEnumeratorDispose)return new CilCallStackEffect(1,null);
        var handle=MetadataTokens.EntityHandle(raw);
        if(handle.Kind==HandleKind.MethodSpecification)handle=md.GetMethodSpecification((MethodSpecificationHandle)handle).Method;
        if(handle.Kind==HandleKind.MemberReference)
        {
            var member=md.GetMemberReference((MemberReferenceHandle)handle);
            var reader=md.GetBlobReader(member.Signature);var header=reader.ReadSignatureHeader();
            if(header.IsGeneric)reader.ReadCompressedInteger();
            var parameters=reader.ReadCompressedInteger();var ret=reader.ReadSignatureTypeCode();
            var hasThis=header.IsInstance&&i.OpCode!=0x73;
            return new CilCallStackEffect(parameters+(hasThis?1:0),i.OpCode==0x73?CilStackKind.ObjectReference:ret==SignatureTypeCode.Void?null:Kind(ret));
        }
        if(handle.Kind==HandleKind.MethodDefinition)
        {
            var def=md.GetMethodDefinition((MethodDefinitionHandle)handle);var reader=md.GetBlobReader(def.Signature);var header=reader.ReadSignatureHeader();
            if(header.IsGeneric)reader.ReadCompressedInteger();
            var parameters=reader.ReadCompressedInteger();var ret=reader.ReadSignatureTypeCode();
            var hasThis=(def.Attributes&System.Reflection.MethodAttributes.Static)==0&&i.OpCode!=0x73;
            return new CilCallStackEffect(parameters+(hasThis?1:0),i.OpCode==0x73?CilStackKind.ObjectReference:ret==SignatureTypeCode.Void?null:Kind(ret));
        }
        return null;
    }
    private static CilStackKind Kind(SignatureTypeCode code)=>code switch
    {
        SignatureTypeCode.Int64 or SignatureTypeCode.UInt64=>CilStackKind.I8,
        SignatureTypeCode.Single or SignatureTypeCode.Double=>CilStackKind.Float,
        SignatureTypeCode.IntPtr or SignatureTypeCode.UIntPtr=>CilStackKind.NativeInt,
        SignatureTypeCode.String or SignatureTypeCode.Object or SignatureTypeCode.SZArray or SignatureTypeCode.Array=>CilStackKind.ObjectReference,
        SignatureTypeCode.Pointer or SignatureTypeCode.ByReference=>CilStackKind.ManagedPointer,
        _=>CilStackKind.I4
    };

    private static AssemblyModel LoadAssembly(CompilationModel model,string path)
    {
        var stream=File.OpenRead(path);var pe=new PEReader(stream);
        if(!pe.HasMetadata){pe.Dispose();stream.Dispose();throw new InvalidDataException($"{path} is not a managed assembly.");}
        var md=pe.GetMetadataReader();
        var name=md.IsAssembly?md.GetString(md.GetAssemblyDefinition().Name):Path.GetFileNameWithoutExtension(path);
        var assembly=new AssemblyModel{Name=name,Path=path,Stream=stream,PE=pe,Metadata=md};
        model.Assemblies[name]=assembly;MetadataLoader.LoadInto(model,md,name);return assembly;
    }

    private static void EnsureEnumerationContracts(CompilationModel model)
    {
        void Type(string name,params string[] interfaces){if(model.Types.ContainsKey(name))return;var dot=name.LastIndexOf('.');model.Types[name]=new(dot<0?"":name[..dot],dot<0?name:name[(dot+1)..],name,null,0,true,false,interfaces);}
        void Method(string type,string name,bool returnsValue){if(model.Methods.Values.Any(m=>m.Key.TypeName==type&&m.Key.Name==name))return;var key=new MethodKey(type,name,"<contracts>",name);var kind=!returnsValue?(CilStackKind?)null:name is "MoveNext" or "CompareTo" or "Compare" or "Equals" or "GetHashCode"?CilStackKind.I4:CilStackKind.ObjectReference;var count=name switch { "Add" or "Contains" or "Remove" or "get_Item" or "IndexOf" or "RemoveAt" or "CompareTo" or "GetHashCode"=>1,"CopyTo" or "set_Item" or "Insert" or "Compare"=>2,"Equals"=>type=="System.Collections.Generic.IEqualityComparer`1"?2:1,_=>0 };
            model.Methods[key]=new(key,default,false,count,returnsValue,"<contracts>",true,true,true,true,Abi:new GenericAbi(Enumerable.Repeat(type=="System.IComparable"?CilStackKind.ObjectReference:CilStackKind.NativeInt,count).ToArray(),kind));}
        void Delegate(string type,int parameters,bool returnsValue)
        {
            var dot=type.LastIndexOf('.');model.Types[type]=new(type[..dot],type[(dot+1)..],type,"System.MulticastDelegate",0);
            var ctor=new MethodKey(type,".ctor","<contracts>","delegate-ctor");model.Methods[ctor]=new(ctor,default,false,2,false,"<contracts>",DeclaringTypeIsDelegate:true,Abi:new GenericAbi(new[]{CilStackKind.ObjectReference,CilStackKind.NativeInt},null));
            var invoke=new MethodKey(type,"Invoke","<contracts>","delegate-invoke");model.Methods[invoke]=new(invoke,default,false,parameters,returnsValue,"<contracts>",DeclaringTypeIsDelegate:true,Abi:new GenericAbi(Enumerable.Repeat(CilStackKind.NativeInt,parameters).ToArray(),returnsValue?CilStackKind.I4:null));
        }
        Delegate("System.Action",0,false);Delegate("System.Action`1",1,false);Delegate("System.Predicate`1",1,true);Delegate("System.Comparison`1",2,true);Delegate("System.Func`1",0,true);Delegate("System.Func`2",1,true);
        Type("System.IComparable");Method("System.IComparable","CompareTo",true);
        Type("System.IComparable`1");Method("System.IComparable`1","CompareTo",true);
        Type("System.IEquatable`1");Method("System.IEquatable`1","Equals",true);
        Type("System.Collections.Generic.IComparer`1");Method("System.Collections.Generic.IComparer`1","Compare",true);
        Type("System.Collections.Generic.IEqualityComparer`1");Method("System.Collections.Generic.IEqualityComparer`1","Equals",true);Method("System.Collections.Generic.IEqualityComparer`1","GetHashCode",true);
        Type("System.IDisposable");Method("System.IDisposable","Dispose",false);
        Type("System.Collections.IEnumerable");Method("System.Collections.IEnumerable","GetEnumerator",true);
        Type("System.Collections.IEnumerator");Method("System.Collections.IEnumerator","get_Current",true);Method("System.Collections.IEnumerator","MoveNext",true);Method("System.Collections.IEnumerator","Reset",false);
        Type("System.Collections.Generic.IEnumerable`1","System.Collections.IEnumerable");Method("System.Collections.Generic.IEnumerable`1","GetEnumerator",true);
        Type("System.Collections.Generic.IEnumerator`1","System.IDisposable","System.Collections.IEnumerator");Method("System.Collections.Generic.IEnumerator`1","get_Current",true);
        Type("System.Collections.Generic.ICollection`1","System.Collections.Generic.IEnumerable`1");Method("System.Collections.Generic.ICollection`1","get_Count",true);Method("System.Collections.Generic.ICollection`1","get_IsReadOnly",true);Method("System.Collections.Generic.ICollection`1","Add",false);Method("System.Collections.Generic.ICollection`1","Clear",false);Method("System.Collections.Generic.ICollection`1","Contains",true);Method("System.Collections.Generic.ICollection`1","CopyTo",false);Method("System.Collections.Generic.ICollection`1","Remove",true);
        Type("System.Collections.Generic.IList`1","System.Collections.Generic.ICollection`1");Method("System.Collections.Generic.IList`1","get_Item",true);Method("System.Collections.Generic.IList`1","set_Item",false);Method("System.Collections.Generic.IList`1","IndexOf",true);Method("System.Collections.Generic.IList`1","Insert",false);Method("System.Collections.Generic.IList`1","RemoveAt",false);
        Type("System.Collections.Generic.IReadOnlyCollection`1","System.Collections.Generic.IEnumerable`1");Method("System.Collections.Generic.IReadOnlyCollection`1","get_Count",true);
        Type("System.Collections.Generic.IReadOnlyList`1","System.Collections.Generic.IReadOnlyCollection`1");Method("System.Collections.Generic.IReadOnlyList`1","get_Item",true);
    }

    private static void LoadDolphinDependencies(CompilationModel model,AssemblyModel root)
    {
        var queue=new Queue<AssemblyModel>();queue.Enqueue(root);
        while(queue.Count>0)
        {
            var current=queue.Dequeue();var dir=Path.GetDirectoryName(current.Path)!;
            foreach(var rh in current.Metadata.AssemblyReferences)
            {
                var r=current.Metadata.GetAssemblyReference(rh);var name=current.Metadata.GetString(r.Name);
                if(model.Assemblies.ContainsKey(name)||!name.StartsWith("DolphinDotNet",StringComparison.Ordinal))continue;
                var candidate=Path.Combine(dir,name+".dll");
                if(!File.Exists(candidate))throw new FileNotFoundException($"Referenced DolphinDotNet assembly '{name}' was not found beside {current.Path}.",candidate);
                queue.Enqueue(LoadAssembly(model,candidate));
            }
        }
    }
    private static string Full(string ns,string name)=>string.IsNullOrEmpty(ns)?name:ns+"."+name;
}
