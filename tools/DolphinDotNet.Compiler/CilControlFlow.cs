namespace DolphinDotNet.Compiler;

internal enum CilFlowKind { Next, Branch, ConditionalBranch, Switch, Return, Throw }

internal abstract record CilOperand;
internal sealed record CilBranchTarget(int Offset):CilOperand;
internal sealed record CilMetadataToken(int Token):CilOperand;
internal sealed record CilSwitchTargets(IReadOnlyList<int> Offsets):CilOperand;
internal sealed record CilInteger(long Value):CilOperand;

internal sealed record CilInstruction(int Offset, int Size, ushort OpCode, CilOperand? Operand, CilFlowKind Flow)
{
    public int EndOffset => Offset + Size;
}

internal static class CilDecoder
{
    public static List<CilInstruction> Decode(byte[] il)
    {
        var result = new List<CilInstruction>();
        for (var p = 0; p < il.Length;)
        {
            var start = p;
            ushort op = il[p++];
            if (op == 0xfe)
            {
                Need(il,p,1,start);
                op=(ushort)(0xfe00|il[p++]);
            }

            CilOperand? operand=null;
            var flow=CilFlowKind.Next;
            switch(op)
            {
                case 0x2a: flow=CilFlowKind.Return; break;
                case 0x7a or 0xfe1a: flow=CilFlowKind.Throw; break;
                case 0x2b or 0xde: operand=new CilBranchTarget(ShortTarget(il,ref p,start)); flow=CilFlowKind.Branch; break;
                case >=0x2c and <=0x37: operand=new CilBranchTarget(ShortTarget(il,ref p,start)); flow=CilFlowKind.ConditionalBranch; break;
                case 0x38 or 0xdd: operand=new CilBranchTarget(LongTarget(il,ref p,start)); flow=CilFlowKind.Branch; break;
                case 0x45: operand=ReadSwitchTargets(il,ref p,start); flow=CilFlowKind.Switch; break;
                case >=0x39 and <=0x44: operand=new CilBranchTarget(LongTarget(il,ref p,start)); flow=CilFlowKind.ConditionalBranch; break;
                default:
                    var operandSize=OperandSize(op,il,p,start);
                    if(op==0x1f)operand=new CilInteger((sbyte)il[p]);
                    else if(op==0x20)operand=new CilInteger(BitConverter.ToInt32(il,p));
                    else if(op==0x21)operand=new CilInteger(BitConverter.ToInt64(il,p));
                    else if(op is 0x0e or 0x0f or 0x10 or 0x11 or 0x12 or 0x13)operand=new CilInteger(il[p]);
                    else if(op is 0xfe09 or 0xfe0a or 0xfe0b or 0xfe0c or 0xfe0d or 0xfe0e)operand=new CilInteger(BitConverter.ToUInt16(il,p));
                    else if(op is 0x28 or 0x6f or 0x72 or 0x73 or 0x7b or 0x7d or 0x7e or 0x80 or 0x8c or 0x8d or 0x8f or 0xa3 or 0xa4 or 0xa5 or 0x74 or 0x75 or 0x70 or 0x71 or 0x81 or 0xfe06 or 0xfe07 or 0xfe15 or 0xfe16)operand=new CilMetadataToken(BitConverter.ToInt32(il,p));
                    p += operandSize; break;
            }
            result.Add(new CilInstruction(start,p-start,op,operand,flow));
        }
        return result;
    }

    private static CilSwitchTargets ReadSwitchTargets(byte[] il,ref int p,int start){Need(il,p,4,start);var n=BitConverter.ToInt32(il,p);p+=4;if(n<0)throw new InvalidDataException($"Invalid switch at IL_{start:x4}.");Need(il,p,checked(n*4),start);var baseOffset=p+n*4;var targets=new int[n];for(var i=0;i<n;i++){targets[i]=baseOffset+BitConverter.ToInt32(il,p);p+=4;}return new CilSwitchTargets(targets);}
    private static int ShortTarget(byte[] il,ref int p,int start){Need(il,p,1,start);var delta=(sbyte)il[p++];return p+delta;}
    private static int LongTarget(byte[] il,ref int p,int start){Need(il,p,4,start);var delta=BitConverter.ToInt32(il,p);p+=4;return p+delta;}

    private static int OperandSize(ushort op,byte[] il,int p,int start)
    {
        var size=op switch {
            0x0e or 0x0f or 0x10 or 0x11 or 0x12 or 0x13 or 0x1f => 1,
            0x20 or 0x22 or 0x28 or 0x6f or 0x70 or 0x71 or 0x72 or 0x74 or 0x75 or 0x73 or 0x7b or 0x7c or 0x7d or 0x7e or 0x7f or 0x80 or 0x81 or 0x8c or 0x8d or 0xa3 or 0xa4 or 0xa5 or 0xfe06 or 0xfe07 or 0xfe15 or 0xfe16 => 4,
            0x21 or 0x23 => 8,
            0xfe09 or 0xfe0a or 0xfe0b or 0xfe0c or 0xfe0d or 0xfe0e => 2,
            0x45 => SwitchSize(il,p,start),
            _ => 0
        };
        Need(il,p,size,start);return size;
    }
    private static int SwitchSize(byte[] il,int p,int start){Need(il,p,4,start);var n=BitConverter.ToInt32(il,p);if(n<0)throw new InvalidDataException($"Invalid switch at IL_{start:x4}.");return checked(4+n*4);}
    private static void Need(byte[] il,int p,int n,int start){if(p+n>il.Length)throw new InvalidDataException($"Truncated CIL at IL_{start:x4}.");}
}

internal sealed record CilBasicBlock(int Id,int StartOffset,List<CilInstruction> Instructions,List<int> Successors,List<int> Predecessors);

internal static class CilControlFlowGraph
{
    public static List<CilBasicBlock> Build(IReadOnlyList<CilInstruction> instructions,IReadOnlyList<System.Reflection.Metadata.ExceptionRegion>? exceptionRegions=null)
    {
        var regions=exceptionRegions??Array.Empty<System.Reflection.Metadata.ExceptionRegion>();
        if(instructions.Count==0)return [];
        var starts=new HashSet<int>{instructions[0].Offset};
        foreach(var region in regions) {
            starts.Add(region.TryOffset);
            starts.Add(region.HandlerOffset);
            if(region.Kind==System.Reflection.Metadata.ExceptionRegionKind.Filter) starts.Add(region.FilterOffset);
        }
        foreach(var i in instructions)
        {
            if(i.Operand is CilBranchTarget { Offset: var target })starts.Add(target);
            if(i.Operand is CilSwitchTargets sw)foreach(var switchTarget in sw.Offsets)starts.Add(switchTarget);
            if(i.Flow is CilFlowKind.Branch or CilFlowKind.ConditionalBranch or CilFlowKind.Switch or CilFlowKind.Return or CilFlowKind.Throw && i!=instructions[^1])starts.Add(i.EndOffset);
        }
        var ordered=starts.OrderBy(x=>x).ToArray();
        var byStart=ordered.Select((x,n)=>(x,n)).ToDictionary(x=>x.x,x=>x.n);
        var blocks=new List<CilBasicBlock>();
        for(var n=0;n<ordered.Length;n++)
        {
            var start=ordered[n];var end=n+1<ordered.Length?ordered[n+1]:int.MaxValue;
            var body=instructions.Where(i=>i.Offset>=start&&i.Offset<end).ToList();
            if(body.Count==0)throw new InvalidDataException($"Branch target IL_{start:x4} is not an instruction boundary.");
            var last=body[^1];var successors=new List<int>();
            if(last.Operand is CilBranchTarget { Offset: var target })
            {
                if(last.OpCode is 0xdd or 0xde)
                {
                    // Keep the original leave target; the outgoing edge first enters
                    // the innermost finally that is exited by this leave.
                    var handler=regions.Where(r=>r.Kind==System.Reflection.Metadata.ExceptionRegionKind.Finally &&
                        last.Offset>=r.TryOffset && last.Offset<r.TryOffset+r.TryLength &&
                        (target<r.TryOffset || target>=r.TryOffset+r.TryLength))
                        .OrderBy(r=>r.TryLength).FirstOrDefault();
                    successors.Add(byStart[handler.Kind==System.Reflection.Metadata.ExceptionRegionKind.Finally?handler.HandlerOffset:target]);
                    // Also make the normal destination reachable for stack analysis.
                    // The backend executes the finally before transferring there.
                    if(handler.Kind==System.Reflection.Metadata.ExceptionRegionKind.Finally &&
                        !regions.Any(r=>r.HandlerOffset==target && r.Kind is System.Reflection.Metadata.ExceptionRegionKind.Catch or System.Reflection.Metadata.ExceptionRegionKind.Filter))
                        successors.Add(byStart[target]);
                }
                else successors.Add(byStart[target]);
            }
            // endfinally resumes a continuation chosen dynamically by the backend.
            // It has no statically valid outgoing edge: adding one can merge a
            // normal empty stack with a catch handler's exception-object stack.
            if(last.Operand is CilSwitchTargets sw)foreach(var switchTarget in sw.Offsets)successors.Add(byStart[switchTarget]);
            if(last.Flow==CilFlowKind.Switch && byStart.TryGetValue(last.EndOffset,out var switchFall))successors.Add(switchFall);
            else if(last.Flow==CilFlowKind.ConditionalBranch && byStart.TryGetValue(last.EndOffset,out var fall))successors.Add(fall);
            else if(last.Flow==CilFlowKind.Next && last.OpCode!=0xdc && byStart.TryGetValue(last.EndOffset,out var next))successors.Add(next);
            blocks.Add(new CilBasicBlock(n,start,body,successors,new List<int>()));
        }
        foreach(var block in blocks)foreach(var successor in block.Successors)blocks[successor].Predecessors.Add(block.Id);
        return blocks;
    }
}
