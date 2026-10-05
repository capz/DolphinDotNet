using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace DolphinDotNet.Compiler;

internal sealed record AotCompilation(CompilationModel Model,DependencyGraph Graph,List<IrMethod> Methods);

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

            var graph=new DependencyGraph();var output=new List<IrMethod>();var queue=new Queue<MethodModel>();var queued=new HashSet<MethodKey>();
            queue.Enqueue(entryMethod);queued.Add(entryMethod.Key);
            while(queue.Count>0)
            {
                var method=queue.Dequeue();
                var assembly=model.Assemblies[method.AssemblyName];
                var body=assembly.PE.GetMethodBody(assembly.Metadata.GetMethodDefinition(method.Handle).RelativeVirtualAddress);
                var ilBytes=body.GetILBytes()??throw new InvalidDataException($"{method.Key} has no IL body.");
                var cil=CilDecoder.Decode(ilBytes);
                var cfg=CilControlFlowGraph.Build(cil);
                _=CilStackAnalyzer.Analyze(cfg);
                var ir=IlImporter.Import(assembly.PE,model,method,graph);output.Add(ir);
                foreach(var key in graph.Methods)
                    if(queued.Add(key)&&model.Methods.TryGetValue(key,out var reachable))queue.Enqueue(reachable);
            }
            return new AotCompilation(model,graph,output);
        }
        catch { model.Dispose(); throw; }
    }

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
