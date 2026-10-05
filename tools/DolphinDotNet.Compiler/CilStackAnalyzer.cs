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
            case 0x0e or 0x11 or 0xfe09 or 0xfe0c: Push(s,CilStackKind.Unknown); break;
            case 0x10 or 0x13 or 0xfe0b or 0xfe0e: Pop(s,i); break;
            case 0x0f or 0x12 or 0xfe0a or 0xfe0d: Push(s,CilStackKind.ManagedPointer); break;
            case >=0x15 and <=0x20: Push(s,CilStackKind.I4); break;
            case 0x21: Push(s,CilStackKind.I8); break;
            case 0x22 or 0x23: Push(s,CilStackKind.Float); break;
            case 0x25: if(s.Count==0)Underflow(i); Push(s,s[^1]); break;
            case 0x26: Pop(s,i); break;
            case 0x2b or 0x38: break;
            case 0x45: Pop(s,i); break;
            case 0x2c or 0x2d or 0x39 or 0x3a: Pop(s,i); break;
            case >=0x2e and <=0x37 or >=0x3b and <=0x44: Pop(s,i);Pop(s,i);break;
            case 0x58 or 0x59 or 0x5a or 0x5b or 0x5d or 0x5e or 0x5f or 0x60 or 0x61 or 0x62 or 0x63 or 0x64:
                var r=Pop(s,i);var l=Pop(s,i);Push(s,l==r?l:CilStackKind.Unknown);break;
            case >=0x67 and <=0x6e: Pop(s,i);Push(s,i.OpCode is 0x6a or 0x6e?CilStackKind.I8:i.OpCode is 0x6b or 0x6c?CilStackKind.Float:CilStackKind.I4);break;
            case 0xd3 or 0xe0: Pop(s,i);Push(s,CilStackKind.NativeInt);break;
            case 0x72: Push(s,CilStackKind.ObjectReference); break;
            case 0x74 or 0x75: Pop(s,i);Push(s,CilStackKind.ObjectReference);break;
            case 0x7b: Pop(s,i);Push(s,CilStackKind.Unknown);break;
            case 0x7d: Pop(s,i);Pop(s,i);break;
            case 0x7e: Push(s,CilStackKind.Unknown);break;
            case 0x80: Pop(s,i);break;
            case 0x8c: Pop(s,i);Push(s,CilStackKind.ObjectReference);break;
            case 0x8d: Pop(s,i);Push(s,CilStackKind.ObjectReference);break;
            case 0x8e: Pop(s,i);Push(s,CilStackKind.NativeInt);break;
            case 0x8f: Pop(s,i);Pop(s,i);Push(s,CilStackKind.ManagedPointer);break;
            case 0x70: Pop(s,i);Pop(s,i);break;
            case 0x71: Pop(s,i);Push(s,CilStackKind.I4);break;
            case 0x81: Pop(s,i);Pop(s,i);break;
            case 0xfe15: Pop(s,i);break;
            case 0xfe06: Push(s,CilStackKind.NativeInt);break;
            case 0xfe07: Pop(s,i);Push(s,CilStackKind.NativeInt);break;
            case >=0x46 and <=0x49 or 0x4a: Pop(s,i);Push(s,CilStackKind.I4);break;
            case 0x4c: Pop(s,i);Push(s,CilStackKind.I8);break;
            case 0x50: Pop(s,i);Push(s,CilStackKind.ObjectReference);break;
            case >=0x51 and <=0x57: Pop(s,i);Pop(s,i);break;
            case 0x94 or 0x9a or 0xa3: Pop(s,i);Pop(s,i);Push(s,i.OpCode==0x9a?CilStackKind.ObjectReference:CilStackKind.I4);break;
            case 0x9e or 0xa2 or 0xa4: Pop(s,i);Pop(s,i);Pop(s,i);break;
            case 0xa5: Pop(s,i);Push(s,CilStackKind.Unknown);break;
            case 0xfe01 or 0xfe02 or 0xfe03 or 0xfe04 or 0xfe05: Pop(s,i);Pop(s,i);Push(s,CilStackKind.I4);break;
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
