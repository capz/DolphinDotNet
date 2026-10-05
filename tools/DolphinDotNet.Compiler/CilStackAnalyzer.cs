namespace DolphinDotNet.Compiler;

internal enum CilStackKind { Unknown, I4, I8, NativeInt, Float, ObjectReference, ManagedPointer }

internal sealed record CilStackState(IReadOnlyList<CilStackKind> Values)
{
    public static readonly CilStackState Empty=new(Array.Empty<CilStackKind>());
}

internal sealed record CilStackAnalysis(
    IReadOnlyDictionary<int,CilStackState> EntryStates,
    IReadOnlyDictionary<int,CilStackState> ExitStates,
    IReadOnlyDictionary<int,CilStackState> InstructionEntryStates,
    IReadOnlyDictionary<int,CilStackState> InstructionExitStates);

internal sealed record CilCallStackEffect(int PopCount,CilStackKind? PushKind);

internal static class CilStackAnalyzer
{
    public static CilStackAnalysis Analyze(IReadOnlyList<CilBasicBlock> blocks,Func<CilInstruction,CilCallStackEffect?>? resolveCall=null,bool returnsValue=false)
    {
        if(blocks.Count==0)return new CilStackAnalysis(new Dictionary<int,CilStackState>(),new Dictionary<int,CilStackState>(),new Dictionary<int,CilStackState>(),new Dictionary<int,CilStackState>());
        var entry=new Dictionary<int,CilStackState>{{blocks[0].Id,CilStackState.Empty}};
        var exit=new Dictionary<int,CilStackState>();
        var instructionEntry=new Dictionary<int,CilStackState>();
        var instructionExit=new Dictionary<int,CilStackState>();
        var queue=new Queue<int>();queue.Enqueue(blocks[0].Id);
        while(queue.Count>0)
        {
            var id=queue.Dequeue();var block=blocks[id];
            var stack=entry[id].Values.ToList();
            foreach(var i in block.Instructions){instructionEntry[i.Offset]=new CilStackState(stack.ToArray());Apply(i,stack,resolveCall,returnsValue);instructionExit[i.Offset]=new CilStackState(stack.ToArray());}
            var state=new CilStackState(stack.ToArray());exit[id]=state;
            foreach(var successor in block.Successors)
            {
                if(!entry.TryGetValue(successor,out var existing)){entry[successor]=state;queue.Enqueue(successor);continue;}
                var merged=Merge(existing,state,blocks[successor].StartOffset);
                if(!merged.Equals(existing)){entry[successor]=merged;queue.Enqueue(successor);}
            }
        }
        return new CilStackAnalysis(entry,exit,instructionEntry,instructionExit);
    }

    private static CilStackState Merge(CilStackState a,CilStackState b,int offset)
    {
        if(a.Values.Count!=b.Values.Count)throw new InvalidDataException($"CIL stack height mismatch at IL_{offset:x4}: {a.Values.Count} vs {b.Values.Count}.");
        var values=new CilStackKind[a.Values.Count];var changed=false;
        for(var i=0;i<values.Length;i++){values[i]=a.Values[i]==b.Values[i]?a.Values[i]:CilStackKind.Unknown;changed|=values[i]!=a.Values[i];}
        return changed?new CilStackState(values):a;
    }

    private static void Apply(CilInstruction i,List<CilStackKind> s,Func<CilInstruction,CilCallStackEffect?>? resolveCall,bool returnsValue)
    {
        switch(i.OpCode)
        {
            case >=0x02 and <=0x05: Push(s,CilStackKind.Unknown); break; // ldarg.*
            case >=0x06 and <=0x09: Push(s,CilStackKind.Unknown); break; // ldloc.*
            case >=0x0a and <=0x0d: Pop(s,i); break; // stloc.*
            case 0x0e or 0x11: Push(s,CilStackKind.Unknown); break;
            case 0x13: Pop(s,i); break;
            case >=0x15 and <=0x20: Push(s,CilStackKind.I4); break;
            case 0x21: Push(s,CilStackKind.I8); break;
            case 0x22 or 0x23: Push(s,CilStackKind.Float); break;
            case 0x25: if(s.Count==0)Underflow(i); Push(s,s[^1]); break;
            case 0x26: Pop(s,i); break;
            case 0x2b or 0x38: break;
            case 0x2c or 0x2d or 0x39 or 0x3a: Pop(s,i); break;
            case >=0x2e and <=0x37 or >=0x3b and <=0x44: Pop(s,i);Pop(s,i);break;
            case 0x58 or 0x59 or 0x5a or 0x5b or 0x5d or 0x5e or 0x5f or 0x60 or 0x61 or 0x62 or 0x63 or 0x64:
                var r=Pop(s,i);var l=Pop(s,i);Push(s,l==r?l:CilStackKind.Unknown);break;
            case 0x72: Push(s,CilStackKind.ObjectReference); break;
            case 0x7b: Pop(s,i);Push(s,CilStackKind.Unknown);break;
            case 0x7d: Pop(s,i);Pop(s,i);break;
            case 0xfe01 or 0xfe02 or 0xfe03: Pop(s,i);Pop(s,i);Push(s,CilStackKind.I4);break;
            case 0x28 or 0x6f or 0x73:
                var call=resolveCall?.Invoke(i)??throw new InvalidDataException($"Missing call signature at IL_{i.Offset:x4}.");
                for(var n=0;n<call.PopCount;n++)Pop(s,i);
                if(call.PushKind is { } kind)Push(s,kind);
                break;
            case 0x2a:
                if(returnsValue)Pop(s,i);
                if(s.Count!=0)throw new InvalidDataException($"CIL return at IL_{i.Offset:x4} leaves {s.Count} value(s) on the evaluation stack.");
                break;
        }
    }
    private static CilStackKind Pop(List<CilStackKind>s,CilInstruction i){if(s.Count==0)Underflow(i);var v=s[^1];s.RemoveAt(s.Count-1);return v;}
    private static void Push(List<CilStackKind>s,CilStackKind v)=>s.Add(v);
    private static void Underflow(CilInstruction i)=>throw new InvalidDataException($"CIL evaluation stack underflow at IL_{i.Offset:x4}.");
}
