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
                CilStackAnalysis stackAnalysis;try{stackAnalysis=CilStackAnalyzer.Analyze(cfg,i=>ResolveCallEffect(assembly.Metadata,model,i),method.ReturnsValue);}catch(Exception ex){throw new InvalidDataException($"Stack analysis failed for {method.Key}: {ex.Message}",ex);}
                var valueIr=ValueIrImporter.Import(method,cfg,stackAnalysis,ReadLocalStorage(assembly,method,model),i=>ResolveCall(assembly.Metadata,model,i),i=>ResolveCallEffect(assembly.Metadata,model,i),i=>IsIgnoredCall(assembly.Metadata,i),i=>ResolveIntrinsic(assembly.Metadata,i),i=>NullableValueSize(assembly.Metadata,model,i),i=>GenericArguments(assembly.Metadata,model,i),i=>ResolveString(assembly.Metadata,i),i=>ResolveField(assembly.Metadata,model,i),i=>ResolveType(assembly.Metadata,i));
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
        foreach(var method in model.Methods.Values)
            if(method.IsVirtual&&graph.Types.Contains(method.Key.TypeName)&&model.Assemblies.TryGetValue(method.AssemblyName,out var assembly)&&assembly.Metadata.GetMethodDefinition(method.Handle).RelativeVirtualAddress!=0)graph.AddMethod(method.Key);
    }

    private static void Discover(ValueIrMethod method,CompilationModel model,DependencyGraph graph)
    {
        graph.AddMethod(method.Key);graph.AddType(method.Key.TypeName);AddTypeClosure(method.Key.TypeName,model,graph);
        foreach(var instruction in method.Blocks.SelectMany(b=>b.Instructions))
        {
            switch(instruction)
            {
                case ValueIrLoadFunction fn: graph.AddMethod(fn.Target);graph.AddType(fn.Target.TypeName);break;
                case ValueIrCall call: if(!model.Methods.TryGetValue(call.Target,out var called)||!called.IsAbstract)graph.AddMethod(call.Target);graph.AddType(call.Target.TypeName);AddTypeClosure(call.Target.TypeName,model,graph);break;
                case ValueIrNewDelegate: break;
                case ValueIrDelegateInvoke: break;
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
        var signature=assembly.Metadata.GetStandaloneSignature(body.LocalSignature);var reader=assembly.Metadata.GetBlobReader(signature.Signature);reader.ReadSignatureHeader();
        var count=reader.ReadCompressedInteger();var locals=new LocalStorage[count];
        for(var i=0;i<count;i++)
        {
            var start=reader.Offset;var code=reader.ReadSignatureTypeCode();
            if(code==SignatureTypeCode.GenericTypeInstance)
            {
                reader.Offset=start;var layout=GenericSharing.ReadGenericLocalLayout(assembly.Metadata,ref reader,model);locals[i]=new LocalStorage(layout.Size,layout.Size>0?CilStackKind.ManagedPointer:CilStackKind.ObjectReference,layout.References);
            }
            else
            {
                SkipLocalType(assembly.Metadata,ref reader,code,model);
                locals[i]=new LocalStorage(0,code==SignatureTypeCode.TypeHandle?CilStackKind.NativeInt:Kind(code));
            }
        }
        return locals;
    }
    private static void SkipLocalType(MetadataReader md,ref BlobReader reader,SignatureTypeCode code,CompilationModel model)
    {
        if(code==SignatureTypeCode.TypeHandle)reader.ReadTypeHandle();
        else if(code is SignatureTypeCode.ByReference or SignatureTypeCode.Pointer)GenericSharing.ReadRepresentation(md,ref reader,model);
        else if(code==SignatureTypeCode.SZArray)GenericSharing.ReadRepresentation(md,ref reader,model);
    }

    private static MethodModel? ResolveCall(MetadataReader md,CompilationModel model,CilInstruction i)
    {
        if(i.OpCode is not (0x28 or 0x6f or 0x73 or 0xfe06 or 0xfe07)||i.Operand is not CilMetadataToken { Token: var raw })return null;
        try{return IlImporter.ResolveMethod(md,model,MetadataTokens.EntityHandle(raw));}
        catch(NotSupportedException){return null;}\n    }
