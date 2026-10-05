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
            var entryKey=new MethodKey(Full(root.Metadata.GetString(td.Namespace),root.Metadata.GetString(td.Name)),root.Metadata.GetString(def.Name));
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
                var cfg=CilControlFlowGraph.Build(cil);
                var stackAnalysis=CilStackAnalyzer.Analyze(cfg,i=>ResolveCallEffect(assembly.Metadata,model,i),method.ReturnsValue);
                var valueIr=ValueIrImporter.Import(method,cfg,stackAnalysis,ReadLocalCount(assembly,method),i=>ResolveCall(assembly.Metadata,model,i));
                valueOutput.Add(valueIr);
                var ir=IlImporter.Import(assembly.PE,model,method,graph);output.Add(ir);
                foreach(var key in graph.Methods)
                    if(queued.Add(key)&&model.Methods.TryGetValue(key,out var reachable))queue.Enqueue(reachable);
            }
            return new AotCompilation(model,graph,output,valueOutput);
        }
        catch { model.Dispose(); throw; }
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
        if(i.OpCode is not (0x28 or 0x6f)||i.Operand is not CilMetadataToken { Token: var raw })return null;
        var handle=MetadataTokens.EntityHandle(raw);string? type=null,name=null;
        if(handle.Kind==HandleKind.MethodDefinition){var d=md.GetMethodDefinition((MethodDefinitionHandle)handle);name=md.GetString(d.Name);var t=md.GetTypeDefinition(d.GetDeclaringType());type=Full(md.GetString(t.Namespace),md.GetString(t.Name));}
        else if(handle.Kind==HandleKind.MemberReference){var m=md.GetMemberReference((MemberReferenceHandle)handle);name=md.GetString(m.Name);type=MetadataLoader.ResolveTypeName(md,m.Parent);}
        return type!=null&&name!=null&&model.Methods.TryGetValue(new MethodKey(type,name),out var method)?method:null;
    }

    private static CilCallStackEffect? ResolveCallEffect(MetadataReader md,CompilationModel model,CilInstruction i)
    {
        if(i.OpCode is not (0x28 or 0x6f or 0x73)||i.Operand is not CilMetadataToken { Token: var raw })return null;
        var handle=MetadataTokens.EntityHandle(raw);
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
