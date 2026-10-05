using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace DolphinDotNet.Compiler;

internal sealed record AotCompilation(CompilationModel Model,DependencyGraph Graph,List<IrMethod> Methods);

internal static class AotCompiler
{
    public static AotCompilation Compile(string path)
    {
        using var stream=File.OpenRead(path); using var pe=new PEReader(stream);
        if(!pe.HasMetadata) throw new InvalidDataException("Input is not a managed assembly.");
        var md=pe.GetMetadataReader(); var model=MetadataLoader.Load(md);
        var cor=pe.PEHeaders.CorHeader??throw new InvalidDataException("Missing CLI header.");
        if(cor.EntryPointTokenOrRelativeVirtualAddress==0)throw new InvalidDataException("Assembly has no managed entry point.");
        var entry=MetadataTokens.EntityHandle(cor.EntryPointTokenOrRelativeVirtualAddress);
        if(entry.Kind!=HandleKind.MethodDefinition)throw new NotSupportedException("Only MethodDef entry points are supported.");
        var def=md.GetMethodDefinition((MethodDefinitionHandle)entry);var td=md.GetTypeDefinition(def.GetDeclaringType());
        var entryKey=new MethodKey(Full(md.GetString(td.Namespace),md.GetString(td.Name)),md.GetString(def.Name));
        if(!model.Methods.TryGetValue(entryKey,out var entryMethod))throw new InvalidDataException("Entry point missing from model.");

        var graph=new DependencyGraph();var output=new List<IrMethod>();var queue=new Queue<MethodModel>();var queued=new HashSet<MethodKey>();
        queue.Enqueue(entryMethod);queued.Add(entryMethod.Key);
        while(queue.Count>0)
        {
            var method=queue.Dequeue(); var ir=IlImporter.Import(pe,model,method,graph);output.Add(ir);
            foreach(var key in graph.Methods)
                if(queued.Add(key)&&model.Methods.TryGetValue(key,out var reachable))queue.Enqueue(reachable);
        }
        return new AotCompilation(model,graph,output);
    }
    private static string Full(string ns,string name)=>string.IsNullOrEmpty(ns)?name:ns+"."+name;
}
