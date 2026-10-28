namespace DolphinDotNet.Compiler;

// A small, acyclic leaf with only non-allocating primitive operations cannot
// collect or surrender the managed gate. Its caller keeps its live roots and
// polls normally. Do not infer safety for unknown calls, static initialization, unknown
// intrinsics, exception regions, bulk copies or backward control-flow edges.
internal static class BoundedLeafAnalysis
{
    internal static IReadOnlySet<MethodKey> Analyze(IReadOnlyList<ValueIrMethod> methods,CompilationModel model)
    {
        var graph=methods.ToDictionary(m=>m.Key);var costs=new Dictionary<MethodKey,int>();var visiting=new HashSet<MethodKey>();
        int Cost(MethodKey key)
        {
            if(costs.TryGetValue(key,out var cost))return cost;
            if(!graph.TryGetValue(key,out var method)||!visiting.Add(key))return -1;
            cost=PrimitiveCost(method,model,Cost);visiting.Remove(key);costs[key]=cost;return cost;
        }
        foreach(var method in methods)Cost(method.Key);
        return costs.Where(p=>p.Value>=0).Select(p=>p.Key).ToHashSet();
    }
    internal static bool CanOmitSafepoints(ValueIrMethod method,CompilationModel model)
        =>PrimitiveCost(method,model,_=>-1)>=0;

    private static int PrimitiveCost(ValueIrMethod method,CompilationModel model,Func<MethodKey,int> calleeCost)
    {
        const int budget=256;
        if(method.ExceptionRegions.Count!=0||method.Blocks.Count==0||method.Blocks.Count>32||
           method.Blocks.Sum(b=>b.Instructions.Count)>budget||method.Locals.Any(l=>l.StorageSize>256))return -1;
        if(model.Methods.TryGetValue(method.Key,out var signature)&&signature.Abi is { } abi&&
           (abi.ParameterStorage?.Any(s=>s.Size>256)==true||abi.ReturnStorage is {Size:>256}))return -1;
        var order=method.Blocks.Select((b,i)=>(b.Id,i)).ToDictionary(p=>p.Id,p=>p.i);
        int cost=0;
        foreach(var block in method.Blocks)
        {
            foreach(var instruction in block.Instructions)
            {
                cost++;
                if(instruction is ValueIrCall call)
                {
                    if(call.Virtual||call.Interface||call.ArrayFallback is not null)return -1;
                    int child=calleeCost(call.Target);if(child<0)return -1;cost+=child;
                }
                else if(instruction is ValueIrValueConstructor constructor)
                {
                    if(constructor.Size>256)return -1;
                    int child=calleeCost(constructor.Constructor);if(child<0)return -1;cost+=child;
                }
                else if(instruction is not (ValueIrConstant or ValueIrFloatConstant or
                    ValueIrLoadArgument or ValueIrLoadLocal or ValueIrStoreLocal or
                    ValueIrAddressOfArgument or ValueIrAddressOfLocal or ValueIrStoreArgument or
                    ValueIrLoadField or ValueIrStoreField or ValueIrArrayLength or
                    ValueIrLoadElement or ValueIrLoadIndirect or ValueIrStoreIndirect or
                    ValueIrArrayElementAddress or ValueIrConvert or ValueIrBinary or
                    ValueIrUnary or ValueIrPhi or ValueIrStringLength))return -1;
                if(cost>budget)return -1;
            }
            int[] targets;
            switch(block.Terminator)
            {
                case ValueIrReturn: targets=Array.Empty<int>();break;
                case ValueIrJump jump: targets=new[]{jump.TargetBlock};break;
                case ValueIrBranch branch: targets=new[]{branch.TrueBlock,branch.FalseBlock};break;
                case ValueIrSwitch choice: targets=choice.Targets.Append(choice.DefaultBlock).ToArray();break;
                default:return -1;
            }
            if(targets.Any(target=>!order.TryGetValue(target,out var next)||next<=order[block.Id]))return -1;
        }
        return cost;
    }
}
