namespace DolphinDotNet.Compiler;

internal enum CilFlowKind { Next, Branch, ConditionalBranch, Return }

internal abstract record CilOperand;
internal sealed record CilBranchTarget(int Offset):CilOperand;
internal sealed record CilMetadataToken(int Token):CilOperand;

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
                case 0x2b: operand=new CilBranchTarget(ShortTarget(il,ref p,start)); flow=CilFlowKind.Branch; break;
                case >=0x2c and <=0x37: operand=new CilBranchTarget(ShortTarget(il,ref p,start)); flow=CilFlowKind.ConditionalBranch; break;
                case 0x38: operand=new CilBranchTarget(LongTarget(il,ref p,start)); flow=CilFlowKind.Branch; break;
                case >=0x39 and <=0x44: operand=new CilBranchTarget(LongTarget(il,ref p,start)); flow=CilFlowKind.ConditionalBranch; break;
                default:
                    var operandSize=OperandSize(op,il,p,start);
                    if(op is 0x28 or 0x6f or 0x73){operand=new CilMetadataToken(BitConverter.ToInt32(il,p));}
                    p += operandSize; break;
            }
            result.Add(new CilInstruction(start,p-start,op,operand,flow));
        }
        return result;
    }

    private static int ShortTarget(byte[] il,ref int p,int start){Need(il,p,1,start);var delta=(sbyte)il[p++];return p+delta;}
    private static int LongTarget(byte[] il,ref int p,int start){Need(il,p,4,start);var delta=BitConverter.ToInt32(il,p);p+=4;return p+delta;}

    private static int OperandSize(ushort op,byte[] il,int p,int start)
    {
        var size=op switch {
            0x0e or 0x0f or 0x10 or 0x11 or 0x12 or 0x13 or 0x1f => 1,
            0x20 or 0x22 or 0x28 or 0x6f or 0x70 or 0x72 or 0x73 or 0x7b or 0x7c or 0x7d or 0x7e or 0x7f or 0x80 => 4,
            0x21 or 0x23 => 8,
            0x45 => SwitchSize(il,p,start),
            _ => 0
        };
        Need(il,p,size,start);return size;
    }
    private static int SwitchSize(byte[] il,int p,int start){Need(il,p,4,start);var n=BitConverter.ToInt32(il,p);if(n<0)throw new InvalidDataException($"Invalid switch at IL_{start:x4}.");return checked(4+n*4);}
    private static void Need(byte[] il,int p,int n,int start){if(p+n>il.Length)throw new InvalidDataException($"Truncated CIL at IL_{start:x4}.");}
}

internal sealed record CilBasicBlock(int Id,int StartOffset,List<CilInstruction> Instructions,List<int> Successors);

internal static class CilControlFlowGraph
{
    public static List<CilBasicBlock> Build(IReadOnlyList<CilInstruction> instructions)
    {
        if(instructions.Count==0)return [];
        var starts=new HashSet<int>{instructions[0].Offset};
        foreach(var i in instructions)
        {
            if(i.Operand is CilBranchTarget { Offset: var target })starts.Add(target);
            if(i.Flow is CilFlowKind.ConditionalBranch && i!=instructions[^1])starts.Add(i.EndOffset);
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
            if(last.Operand is CilBranchTarget { Offset: var target })successors.Add(byStart[target]);
            if(last.Flow==CilFlowKind.ConditionalBranch && byStart.TryGetValue(last.EndOffset,out var fall))successors.Add(fall);
            else if(last.Flow==CilFlowKind.Next && byStart.TryGetValue(last.EndOffset,out var next))successors.Add(next);
            blocks.Add(new CilBasicBlock(n,start,body,successors));
        }
        return blocks;
    }
}
