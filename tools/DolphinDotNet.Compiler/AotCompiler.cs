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
                var regions=ReadExceptionRegions(assembly.Metadata,body);
                var cfg=CilControlFlowGraph.Build(cil,regions);
                var stackAnalysis=CilStackAnalyzer.Analyze(cfg,i=>ResolveCallEffect(assembly.Metadata,model,i),method.ReturnsValue,regions);
                var valueIr=ValueIrImporter.Import(method,cfg,stackAnalysis,ReadLocalCount(assembly,method),i=>ResolveCall(assembly.Metadata,model,i),i=>ResolveCallEffect(assembly.Metadata,model,i),i=>IsIgnoredCall(assembly.Metadata,i),i=>ResolveIntrinsic(assembly.Metadata,i),i=>ResolveString(assembly.Metadata,i),i=>ResolveField(assembly.Metadata,model,i),i=>ResolveType(assembly.Metadata,i),t=>model.Types.TryGetValue(t,out var tm)&&tm.IsInterface,t=>model.Types.TryGetValue(t,out var tm)&&tm.BaseType is "System.MulticastDelegate" or "System.Delegate",regions);
                ValueIrVerifier.Verify(valueIr);valueOutput.Add(valueIr);
                Discover(valueIr,model,graph);
                DiscoverVirtuals(model,graph);
                DiscoverTypeInitializers(model,graph);
                try { output.Add(IlImporter.Import(assembly.PE,model,method,new DependencyGraph())); }
                catch(NotSupportedException) { /* Legacy backend is a regression oracle, not a production dependency. */ }
                foreach(var key in graph.Methods)
                    if(queued.Add(key)&&model.Methods.TryGetValue(key,out var reachable)&&model.Assemblies.TryGetValue(reachable.AssemblyName,out var ra)&&ra.Metadata.GetMethodDefinition(reachable.Handle).RelativeVirtualAddress!=0)queue.Enqueue(reachable);
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
                case ValueIrCall call: graph.AddMethod(call.Target);graph.AddType(call.Target.TypeName);break;
                case ValueIrFunctionPointer pointer: graph.AddMethod(pointer.Target);graph.AddType(pointer.Target.TypeName);break;
                case ValueIrNewDelegate created: graph.AddType(created.TypeName);break;
                case ValueIrNewObject created: graph.AddMethod(created.Constructor);graph.AddType(created.TypeName);break;
                case ValueIrLoadField field: graph.AddType(field.TypeName);break;
                case ValueIrStoreField field: graph.AddType(field.TypeName);break;
                case ValueIrLoadStaticField field: graph.AddType(field.TypeName);break;
                case ValueIrStoreStaticField field: graph.AddType(field.TypeName);break;
                case ValueIrTypeTest test: graph.AddType(test.TypeName);break;
                case ValueIrInitValue value: graph.AddType(value.TypeName);break;
                case ValueIrCopyValue value: graph.AddType(value.TypeName);break;
                case ValueIrLoadValue value: graph.AddType(value.TypeName);break;
                case ValueIrStoreValue value: graph.AddType(value.TypeName);break;
                case ValueIrNewArray array: if(!array.ElementType.StartsWith("System.",StringComparison.Ordinal))graph.AddType(array.ElementType);break;
            }
        }
    }


    private static IReadOnlyList<ExceptionRegionModel> ReadExceptionRegions(MetadataReader md,MethodBodyBlock body)
    {
        var result=new List<ExceptionRegionModel>();
        foreach(var region in body.ExceptionRegions)
        {
            var kind=region.Kind switch
            {
                System.Reflection.Metadata.ExceptionRegionKind.Catch=>ExceptionRegionKind.Catch,
                System.Reflection.Metadata.ExceptionRegionKind.Finally=>ExceptionRegionKind.Finally,
                System.Reflection.Metadata.ExceptionRegionKind.Fault=>ExceptionRegionKind.Fault,
                System.Reflection.Metadata.ExceptionRegionKind.Filter=>ExceptionRegionKind.Filter,
                _=>throw new NotSupportedException($"Unsupported exception region kind {region.Kind}.")
            };
            string? catchType=null;
            if(kind==ExceptionRegionKind.Catch&&!region.CatchType.IsNil)catchType=MetadataLoader.ResolveTypeName(md,region.CatchType);
            result.Add(new ExceptionRegionModel(region.TryOffset,region.TryLength,region.HandlerOffset,region.HandlerLength,kind,catchType,region.FilterOffset));
        }
        return result;
    }

    private static int ReadLocalCount(AssemblyModel assembly,MethodModel method)
    {
        var def=assembly.Metadata.GetMethodDefinition(method.Handle);
        var body=assembly.PE.GetMethodBody(def.RelativeVirtualAddress);
        if(body.LocalSignature.IsNil)return 0;
        var signature=assembly.Metadata.GetStandaloneSignature(body.LocalSignature);
        var reader=assembly.Metadata.GetBlobReader(signature.Signature);reader.ReadSignatureHeader();return reader.ReadCompressedInteger();
    }

    private static MethodModel? ResolveCall(MetadataReader md,CompilationModel model,CilInstruction i)
    {
        if(i.OpCode is not (0x28 or 0x6f or 0x73 or 0xfe06 or 0xfe07)||i.Operand is not CilMetadataToken { Token: var raw })return null;
        try{return IlImporter.ResolveMethod(md,model,MetadataTokens.EntityHandle(raw));}
        catch(NotSupportedException){return null;}
    }

    private static string? ResolveType(MetadataReader md,CilInstruction i)
    {
        if(i.Operand is not CilMetadataToken { Token: var raw })return null;
        try{return MetadataLoader.ResolveTypeName(md,MetadataTokens.EntityHandle(raw));}catch{return null;}
    }

    private static FieldModel? ResolveField(MetadataReader md,CompilationModel model,CilInstruction i)
    {
        if(i.OpCode is not (0x7b or 0x7d or 0x7e or 0x80)||i.Operand is not CilMetadataToken { Token: var raw })return null;
        try{return IlImporter.ResolveField(md,model,MetadataTokens.EntityHandle(raw));}catch(NotSupportedException){return null;}
    }

    private static IntrinsicKind ResolveIntrinsic(MetadataReader md,CilInstruction i)
    {
        if(i.OpCode is not (0x28 or 0x6f)||i.Operand is not CilMetadataToken { Token: var raw })return IntrinsicKind.None;
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

    private static CilCallStackEffect? ResolveCallEffect(MetadataReader md,CompilationModel model,CilInstruction i)
    {
        if(i.OpCode is not (0x28 or 0x6f or 0x73)||i.Operand is not CilMetadataToken { Token: var raw })return null;
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
