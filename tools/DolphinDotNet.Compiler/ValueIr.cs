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
internal sealed record ValueIrFloatConstant(IrValue Result,double Value):ValueIrInstruction;
internal sealed record ValueIrConstant(IrValue Result,long Value):ValueIrInstruction;
internal sealed record ValueIrLoadString(IrValue Result,string Value):ValueIrInstruction;
internal sealed record ValueIrLoadArgument(IrValue Result,int Index):ValueIrInstruction;
internal sealed record ValueIrLoadLocal(IrValue Result,int Index):ValueIrInstruction;
internal sealed record ValueIrLocal(int Index,IrValueKind Kind);
internal sealed record ValueIrStoreLocal(int Index,IrValue Value):ValueIrInstruction;
internal sealed record ValueIrAddressOfLocal(IrValue Result,int Index):ValueIrInstruction;
internal sealed record ValueIrAddressOfArgument(IrValue Result,int Index):ValueIrInstruction;
internal sealed record ValueIrStoreArgument(int Index,IrValue Value):ValueIrInstruction;
internal sealed record ValueIrLoadIndirect(IrValue Result,IrValue Address,int Size,bool Reference):ValueIrInstruction;
internal sealed record ValueIrStoreIndirect(IrValue Address,IrValue Value,int Size,bool Reference):ValueIrInstruction;
internal sealed record ValueIrInitValue(IrValue Address,string TypeName):ValueIrInstruction;
internal sealed record ValueIrCopyValue(IrValue Destination,IrValue Source,string TypeName):ValueIrInstruction;
internal sealed record ValueIrLoadValue(IrValue Result,IrValue Address,string TypeName):ValueIrInstruction;
internal sealed record ValueIrStoreValue(IrValue Address,IrValue Value,string TypeName):ValueIrInstruction;
internal sealed record ValueIrConvert(IrValue Result,IrValue Value):ValueIrInstruction;
internal sealed record ValueIrBinary(IrValue Result,string Operation,IrValue Left,IrValue Right):ValueIrInstruction;
internal sealed record ValueIrDelegateInvoke(IrValue? Result,IrValue Delegate,IReadOnlyList<IrValue> Arguments):ValueIrInstruction;
internal sealed record ValueIrFunctionPointer(IrValue Result,MethodKey Target,IrValue? Receiver):ValueIrInstruction;
internal sealed record ValueIrNewDelegate(IrValue Result,string TypeName,IrValue Target,IrValue Function):ValueIrInstruction;
internal sealed record ValueIrCall(IrValue? Result,MethodKey Target,IReadOnlyList<IrValue> Arguments,bool Virtual=false,bool Interface=false):ValueIrInstruction;
internal sealed record ValueIrNewObject(IrValue Result,string TypeName,MethodKey Constructor,IReadOnlyList<IrValue> Arguments):ValueIrInstruction;
internal sealed record ValueIrLoadField(IrValue Result,IrValue Object,string TypeName,string FieldName):ValueIrInstruction;
internal sealed record ValueIrStoreField(IrValue Object,IrValue Value,string TypeName,string FieldName):ValueIrInstruction;
internal sealed record ValueIrLoadStaticField(IrValue Result,string TypeName,string FieldName):ValueIrInstruction;
internal sealed record ValueIrStoreStaticField(IrValue Value,string TypeName,string FieldName):ValueIrInstruction;
internal sealed record ValueIrTypeTest(IrValue Result,IrValue Object,string TypeName,bool ThrowOnFailure):ValueIrInstruction;
internal sealed record ValueIrStringLength(IrValue Result,IrValue String):ValueIrInstruction;
internal sealed record ValueIrObjectReferenceEquals(IrValue Result,IrValue Left,IrValue Right):ValueIrInstruction;
internal sealed record ValueIrObjectGetHashCode(IrValue Result,IrValue Object):ValueIrInstruction;
internal sealed record ValueIrObjectGetType(IrValue Result,IrValue Object):ValueIrInstruction;
internal sealed record ValueIrStringIndexOf(IrValue Result,IrValue String,IrValue Needle):ValueIrInstruction;
internal sealed record ValueIrStringSubstring(IrValue Result,IrValue String,IrValue Start,IrValue? Length):ValueIrInstruction;
internal sealed record ValueIrArrayClear(IrValue Array,IrValue Index,IrValue Length):ValueIrInstruction;
internal sealed record ValueIrArrayCopy(IrValue Source,IrValue SourceIndex,IrValue Destination,IrValue DestinationIndex,IrValue Length):ValueIrInstruction;
internal sealed record ValueIrNewArray(IrValue Result,IrValue Length,string ElementType,bool ElementsAreReferences,uint ElementSize):ValueIrInstruction;
internal sealed record ValueIrBox(IrValue Result,IrValue Value,string TypeName):ValueIrInstruction;
internal sealed record ValueIrUnboxAny(IrValue Result,IrValue Object,string TypeName):ValueIrInstruction;
internal sealed record ValueIrArrayElementAddress(IrValue Result,IrValue Array,IrValue Index,string ElementType):ValueIrInstruction;
internal sealed record ValueIrArrayLength(IrValue Result,IrValue Array):ValueIrInstruction;
internal sealed record ValueIrLoadElement(IrValue Result,IrValue Array,IrValue Index,bool Reference):ValueIrInstruction;
internal sealed record ValueIrStoreElement(IrValue Array,IrValue Index,IrValue Value,bool Reference):ValueIrInstruction;
internal sealed record ValueIrConsoleWriteLine(IrValue String):ValueIrInstruction;
internal sealed record ValueIrReadButtonsDown(IrValue Result,IrValue Port):ValueIrInstruction;
internal sealed record ValueIrPresentDemoFrame(IrValue Rotation):ValueIrInstruction;
internal sealed record ValueIrOpaqueStackEffect(int PopCount,IReadOnlyList<IrValue> Results,ushort OpCode):ValueIrInstruction;
internal sealed record ValueIrPhi(IrValue Result,IReadOnlyDictionary<int,IrValue> Inputs):ValueIrInstruction;
internal sealed record ValueIrIncomingStack(IReadOnlyList<IrValue> Values);

internal abstract record ValueIrTerminator;
internal sealed record ValueIrJump(int TargetBlock):ValueIrTerminator;
internal enum ValueIrComparison { NonZero,Equal,NotEqual,GreaterThan,GreaterOrEqual,LessThan,LessOrEqual }
internal sealed record ValueIrBranch(IrValue Left,IrValue? Right,ValueIrComparison Comparison,bool Unsigned,int TrueBlock,int FalseBlock):ValueIrTerminator;
internal sealed record ValueIrSwitch(IrValue Value,IReadOnlyList<int> Targets,int DefaultBlock):ValueIrTerminator;
internal sealed record ValueIrLeave(int TargetBlock,IReadOnlyList<int> FinallyBlocks):ValueIrTerminator;
internal sealed record ValueIrEndFinally():ValueIrTerminator;
internal sealed record ValueIrRethrow():ValueIrTerminator;
internal sealed record ValueIrThrow(IrValue Exception):ValueIrTerminator;
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
    IReadOnlyList<ValueIrLocal> Locals,
    int ParameterCount,
    bool HasThis,
    bool ReturnsValue,
    IReadOnlyList<ExceptionRegionModel>? ExceptionRegions=null)
{
    public int LocalCount=>Locals.Count;
}
