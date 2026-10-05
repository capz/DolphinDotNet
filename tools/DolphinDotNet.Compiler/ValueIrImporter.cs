namespace DolphinDotNet.Compiler;

internal static class ValueIrImporter
{
    public static ValueIrMethod Import(
        MethodModel method,
        IReadOnlyList<CilBasicBlock> blocks,
        CilStackAnalysis analysis,
        int localCount)
    {
        var nextValue=0;
        IrValue New(CilStackKind kind)=>new(nextValue++,Map(kind));
        var output=new List<ValueIrBlock>();
        var entryValues=new Dictionary<int,List<IrValue>>();
        var exitValues=new Dictionary<int,List<IrValue>>();

        foreach(var block in blocks)
        {
            var instructions=new List<ValueIrInstruction>();
            var stack=new List<IrValue>();
            if(analysis.EntryStates.TryGetValue(block.Id,out var entry))
            {
                if(!entryValues.TryGetValue(block.Id,out var incoming))
                {
                    incoming=entry.Values.Select(New).ToList();
                    entryValues[block.Id]=incoming;
                }
                stack.AddRange(incoming);
            }

            ValueIrTerminator? terminator=null;
            foreach(var cil in block.Instructions)
            {
                switch(cil.OpCode)
                {
                    case >=0x16 and <=0x1e:
                    {
                        var v=New(CilStackKind.I4);instructions.Add(new ValueIrConstant(v,cil.OpCode-0x16));stack.Add(v);break;
                    }
                    case 0x1f:
                    {
                        throw new InvalidDataException($"Value IR decoder requires decoded int8 operand at IL_{cil.Offset:x4}.");
                    }
                    case 0x20:
                    {
                        throw new InvalidDataException($"Value IR decoder requires decoded int32 operand at IL_{cil.Offset:x4}.");
                    }
                    case 0x15:
                    {
                        var v=New(CilStackKind.I4);instructions.Add(new ValueIrConstant(v,-1));stack.Add(v);break;
                    }
                    case >=0x06 and <=0x09:
                    {
                        var v=New(CilStackKind.Unknown);instructions.Add(new ValueIrLoadLocal(v,cil.OpCode-0x06));stack.Add(v);break;
                    }
                    case >=0x0a and <=0x0d:
                        instructions.Add(new ValueIrStoreLocal(cil.OpCode-0x0a,Pop(stack,cil)));break;
                    case >=0x02 and <=0x05:
                    {
                        var v=New(CilStackKind.Unknown);instructions.Add(new ValueIrLoadArgument(v,cil.OpCode-0x02));stack.Add(v);break;
                    }
                    case 0x25:
                    {
                        var value=Pop(stack,cil);stack.Add(value);stack.Add(value);break;
                    }
                    case 0x26:
                        Pop(stack,cil);break;
                    case 0x58 or 0x59 or 0x5a:
                    {
                        var right=Pop(stack,cil);var left=Pop(stack,cil);var result=New(Merge(left.Kind,right.Kind));
                        instructions.Add(new ValueIrBinary(result,cil.OpCode==0x58?"add":cil.OpCode==0x59?"sub":"mul",left,right));stack.Add(result);break;
                    }
                    case 0x2b or 0x38:
                        terminator=new ValueIrJump(Target(blocks,cil));break;
                    case 0x2c or 0x39:
                    {
                        var condition=Pop(stack,cil);var target=Target(blocks,cil);terminator=new ValueIrBranch(condition,Fallthrough(blocks,block,cil),target);break;
                    }
                    case 0x2d or 0x3a:
                    {
                        var condition=Pop(stack,cil);var target=Target(blocks,cil);terminator=new ValueIrBranch(condition,target,Fallthrough(blocks,block,cil));break;
                    }
                    case 0x2a:
                        terminator=new ValueIrReturn(method.ReturnsValue?Pop(stack,cil):null);break;
                }
            }
            exitValues[block.Id]=stack.ToList();
            output.Add(new ValueIrBlock(block.Id,block.StartOffset,instructions,terminator,
                new ValueIrIncomingStack(entryValues.TryGetValue(block.Id,out var ev)?ev.ToArray():Array.Empty<IrValue>()),
                new ValueIrIncomingStack(stack.ToArray())));
        }
        // Materialize stack joins as phi values. The entry value is the stable identity
        // used by instructions in the destination block; predecessor exit values feed it.
        foreach(var block in output)
        {
            if(block.EntryStack.Values.Count==0)continue;
            var predecessors=blocks.Where(b=>b.Successors.Contains(block.Id)).ToArray();
            if(predecessors.Length<2)continue;
            for(var slot=0;slot<block.EntryStack.Values.Count;slot++)
            {
                var inputs=new Dictionary<int,IrValue>();
                foreach(var pred in predecessors)
                    if(exitValues.TryGetValue(pred.Id,out var values)&&slot<values.Count)inputs[pred.Id]=values[slot];
                if(inputs.Count==predecessors.Length)
                    block.Instructions.Insert(slot,new ValueIrPhi(block.EntryStack.Values[slot],inputs));
            }
        }
        return new ValueIrMethod(method.Key,output,localCount,method.ParameterCount,!method.IsStatic,method.ReturnsValue);
    }

    private static IrValue Pop(List<IrValue>s,CilInstruction i){if(s.Count==0)throw new InvalidDataException($"Value IR stack underflow at IL_{i.Offset:x4}.");var v=s[^1];s.RemoveAt(s.Count-1);return v;}
    private static int Target(IReadOnlyList<CilBasicBlock>b,CilInstruction i){var offset=(i.Operand as CilBranchTarget)?.Offset??throw new InvalidDataException("Missing branch target.");return b.Single(x=>x.StartOffset==offset).Id;}
    private static int Fallthrough(IReadOnlyList<CilBasicBlock>b,CilBasicBlock current,CilInstruction i)=>b.Single(x=>x.StartOffset==i.EndOffset).Id;
    private static CilStackKind Merge(IrValueKind a,IrValueKind b)=>a==b?Unmap(a):CilStackKind.Unknown;
    private static IrValueKind Map(CilStackKind k)=>k switch{CilStackKind.I4=>IrValueKind.I4,CilStackKind.I8=>IrValueKind.I8,CilStackKind.NativeInt=>IrValueKind.NativeInt,CilStackKind.Float=>IrValueKind.Float,CilStackKind.ObjectReference=>IrValueKind.ObjectReference,CilStackKind.ManagedPointer=>IrValueKind.ManagedPointer,_=>IrValueKind.Unknown};
    private static CilStackKind Unmap(IrValueKind k)=>k switch{IrValueKind.I4=>CilStackKind.I4,IrValueKind.I8=>CilStackKind.I8,IrValueKind.NativeInt=>CilStackKind.NativeInt,IrValueKind.Float=>CilStackKind.Float,IrValueKind.ObjectReference=>CilStackKind.ObjectReference,IrValueKind.ManagedPointer=>CilStackKind.ManagedPointer,_=>CilStackKind.Unknown};
}
