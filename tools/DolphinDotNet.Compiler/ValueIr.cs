namespace DolphinDotNet.Compiler;

internal enum IrValueKind
{
    Unknown,
    Void,
    I4,
    I8,
    R4,
    R8,
    NativeInt,
    ObjectReference,
    ManagedPointer
}

internal readonly record struct IrValue(int Id,IrValueKind Kind)
{
    public override string ToString()=>$"v{Id}:{Kind}";
}

internal abstract record ValueIrInstruction;
internal sealed record ValueIrConstant(IrValue Result,long Value):ValueIrInstruction;
internal sealed record ValueIrLoadArgument(IrValue Result,int Index):ValueIrInstruction;
internal sealed record ValueIrLoadLocal(IrValue Result,int Index):ValueIrInstruction;
internal sealed record ValueIrStoreLocal(int Index,IrValue Value):ValueIrInstruction;
internal sealed record ValueIrBinary(IrValue Result,string Operation,IrValue Left,IrValue Right):ValueIrInstruction;
internal sealed record ValueIrOpaqueStackEffect(int PopCount,IReadOnlyList<IrValue> Results,ushort OpCode):ValueIrInstruction;
internal sealed record ValueIrPhi(IrValue Result,IReadOnlyDictionary<int,IrValue> Inputs):ValueIrInstruction;
internal sealed record ValueIrIncomingStack(IReadOnlyList<IrValue> Values);

internal abstract record ValueIrTerminator;
internal sealed record ValueIrJump(int TargetBlock):ValueIrTerminator;
internal sealed record ValueIrBranch(IrValue Condition,int TrueBlock,int FalseBlock):ValueIrTerminator;
internal sealed record ValueIrReturn(IrValue? Value):ValueIrTerminator;

internal sealed record ValueIrBlock(
    int Id,
    int CilOffset,
    List<ValueIrInstruction> Instructions,
    ValueIrTerminator? Terminator,
    ValueIrIncomingStack EntryStack,
    ValueIrIncomingStack ExitStack);

internal sealed record ValueIrMethod(
    MethodKey Key,
    IReadOnlyList<ValueIrBlock> Blocks,
    int LocalCount,
    int ParameterCount,
    bool HasThis,
    bool ReturnsValue);
