namespace DolphinDotNet.Compiler;
internal sealed class DependencyGraph{private readonly HashSet<MethodKey> methods=new();private readonly HashSet<string> types=new(StringComparer.Ordinal);public IReadOnlyCollection<MethodKey> Methods=>methods;public IReadOnlyCollection<string> Types=>types;public bool AddMethod(MethodKey key)=>methods.Add(key);public bool AddType(string type)=>types.Add(type);}
