namespace DolphinDotNet.Compiler;

internal static class ValueIrImporter
{
    public static ValueIrMethod Import(
        MethodModel method,
        IReadOnlyList<CilBasicBlock> blocks,
        CilStackAnalysis analysis,
        int localCount,
        Func<CilInstruction,MethodModel?> resolveCall,
        Func<CilInstruction,CilCallStackEffect?> resolveCallEffect,
        Func<CilInstruction,bool> ignoreCall,
        Func<CilInstruction,IntrinsicKind> intrinsic,
        Func<CilInstruction,string?> resolveString,
        Func<CilInstruction,FieldModel?> resolveField,
        Func<CilInstruction,string?> resolveType,
        Func<string,bool>? isInterfaceType=null,
        Func<string,bool>? isDelegateType=null)
    {
        var nextValue=0;
        bool resolveTypeForMethod(string name)=>isInterfaceType?.Invoke(name)??false;
        bool delegateType(string name)=>isDelegateType?.Invoke(name)??false;
        IrValue New(CilStackKind kind)=>new(nextValue++,Map(kind));
        var output=new List<ValueIrBlock>();
        var entryValues=new Dictionary<int,List<IrValue>>();
        var exitValues=new Dictionary<int,List<IrValue>>();

        // Allocate stable identities for every block entry before translating any body.
        // This makes backward edges independent of block traversal order.
        foreach(var block in blocks)
            if(analysis.EntryStates.TryGetValue(block.Id,out var state))
                entryValues[block.Id]=state.Values.Select(New).ToList();

        foreach(var block in blocks)
        {
            var instructions=new List<ValueIrInstruction>();
            var stack=new List<IrValue>();
            if(entryValues.TryGetValue(block.Id,out var incoming))stack.AddRange(incoming);

            ValueIrTerminator? terminator=null;
            foreach(var cil in block.Instructions)
            {
                var beforeCount=stack.Count;
                switch(cil.OpCode)
                {
                    case 0x00: break;
                    case 0x14: { var v=New(CilStackKind.ObjectReference);instructions.Add(new ValueIrConstant(v,0));stack.Add(v);break; }
                    case >=0x16 and <=0x1e:
                    {
                        var v=New(CilStackKind.I4);instructions.Add(new ValueIrConstant(v,cil.OpCode-0x16));stack.Add(v);break;
                    }
                    case 0x1f or 0x20 or 0x21:
                    {
                        var value=(cil.Operand as CilInteger)?.Value??throw new InvalidDataException($"Missing integer operand at IL_{cil.Offset:x4}.");
                        var v=New(cil.OpCode==0x21?CilStackKind.I8:CilStackKind.I4);instructions.Add(new ValueIrConstant(v,value));stack.Add(v);break;
                    }
                    case 0x22 or 0x23:
                    {
                        var value=(cil.Operand as CilFloat)?.Value??throw new InvalidDataException($"Missing floating operand at IL_{cil.Offset:x4}.");var v=new IrValue(nextValue++,cil.OpCode==0x22?IrValueKind.R4:IrValueKind.R8);instructions.Add(new ValueIrFloatConstant(v,value));stack.Add(v);break;
                    }
                    case 0x15:
                    {
                        var v=New(CilStackKind.I4);instructions.Add(new ValueIrConstant(v,-1));stack.Add(v);break;
                    }
                    case 0x11 or 0xfe0c:
                    {
                        var index=(int)((cil.Operand as CilInteger)?.Value??throw new InvalidDataException($"Missing local index at IL_{cil.Offset:x4}."));var v=New(ResultKind(analysis,cil));instructions.Add(new ValueIrLoadLocal(v,index));stack.Add(v);break;
                    }
                    case 0x13 or 0xfe0e:
                    {
                        var index=(int)((cil.Operand as CilInteger)?.Value??throw new InvalidDataException($"Missing local index at IL_{cil.Offset:x4}."));instructions.Add(new ValueIrStoreLocal(index,Pop(stack,cil)));break;
                    }
                    case >=0x06 and <=0x09:
                    {
                        var v=New(ResultKind(analysis,cil));instructions.Add(new ValueIrLoadLocal(v,cil.OpCode-0x06));stack.Add(v);break;
                    }
                    case >=0x0a and <=0x0d:
                        instructions.Add(new ValueIrStoreLocal(cil.OpCode-0x0a,Pop(stack,cil)));break;
                    case >=0x02 and <=0x05:
                    {
                        var v=New(ResultKind(analysis,cil));instructions.Add(new ValueIrLoadArgument(v,cil.OpCode-0x02));stack.Add(v);break;
                    }
                    case 0x0e or 0xfe09:
                    {
                        var index=(int)((cil.Operand as CilInteger)?.Value??throw new InvalidDataException($"Missing argument index at IL_{cil.Offset:x4}."));var v=New(ResultKind(analysis,cil));instructions.Add(new ValueIrLoadArgument(v,index));stack.Add(v);break;
                    }
                    case 0x10 or 0xfe0b:
                    {
                        var index=(int)((cil.Operand as CilInteger)?.Value??throw new InvalidDataException($"Missing argument index at IL_{cil.Offset:x4}."));instructions.Add(new ValueIrStoreArgument(index,Pop(stack,cil)));break;
                    }
                    case 0x0f or 0xfe0a:
                    {
                        var index=(int)((cil.Operand as CilInteger)?.Value??throw new InvalidDataException($"Missing argument index at IL_{cil.Offset:x4}."));var result=New(CilStackKind.ManagedPointer);instructions.Add(new ValueIrAddressOfArgument(result,index));stack.Add(result);break;
                    }
                    case 0x12 or 0xfe0d:
                    {
                        var index=(int)((cil.Operand as CilInteger)?.Value??throw new InvalidDataException($"Missing local index at IL_{cil.Offset:x4}."));var result=New(CilStackKind.ManagedPointer);instructions.Add(new ValueIrAddressOfLocal(result,index));stack.Add(result);break;
                    }
                    case 0x8c:
                    {
                        var type=resolveType(cil)??throw new NotSupportedException($"Unable to resolve boxed type at IL_{cil.Offset:x4}.");var input=Pop(stack,cil);var result=New(CilStackKind.ObjectReference);instructions.Add(new ValueIrBox(result,input,type));stack.Add(result);break;
                    }
                    case 0xa5:
                    {
                        var type=resolveType(cil)??throw new NotSupportedException($"Unable to resolve unboxed type at IL_{cil.Offset:x4}.");var input=Pop(stack,cil);var result=New(ResultKind(analysis,cil));instructions.Add(new ValueIrUnboxAny(result,input,type));stack.Add(result);break;
                    }
                    case 0x8d:
                    {
                        var length=Pop(stack,cil);var type=resolveType(cil)??throw new NotSupportedException($"Unable to resolve array element type at IL_{cil.Offset:x4}.");
                        var reference=IsReferenceType(type);var result=New(CilStackKind.ObjectReference);
                        instructions.Add(new ValueIrNewArray(result,length,type,reference,ElementSize(type)));stack.Add(result);break;
                    }
                    case 0x8e:
                    {
                        var array=Pop(stack,cil);var result=New(CilStackKind.NativeInt);instructions.Add(new ValueIrArrayLength(result,array));stack.Add(result);break;
                    }
                    case 0x8f:
                    {
                        var index=Pop(stack,cil);var array=Pop(stack,cil);var type=resolveType(cil)??throw new NotSupportedException($"Unable to resolve array address element type at IL_{cil.Offset:x4}.");var result=New(CilStackKind.ManagedPointer);instructions.Add(new ValueIrArrayElementAddress(result,array,index,type));stack.Add(result);break;
                    }
                    case 0x94 or 0x9a:
                    {
                        var index=Pop(stack,cil);var array=Pop(stack,cil);var reference=cil.OpCode==0x9a;var result=New(reference?CilStackKind.ObjectReference:CilStackKind.I4);
                        instructions.Add(new ValueIrLoadElement(result,array,index,reference));stack.Add(result);break;
                    }
                    case 0x9e or 0xa2:
                    {
                        var value=Pop(stack,cil);var index=Pop(stack,cil);var array=Pop(stack,cil);instructions.Add(new ValueIrStoreElement(array,index,value,cil.OpCode==0xa2));break;
                    }
                    case >=0x46 and <=0x4a or 0x4c or 0x50:
                    {
                        var address=Pop(stack,cil);var reference=cil.OpCode==0x50;var size=cil.OpCode is 0x46 or 0x47?1:cil.OpCode is 0x48 or 0x49?2:cil.OpCode==0x4c?8:4;var result=New(reference?CilStackKind.ObjectReference:cil.OpCode==0x4c?CilStackKind.I8:CilStackKind.I4);instructions.Add(new ValueIrLoadIndirect(result,address,size,reference));stack.Add(result);break;
                    }
                    case >=0x51 and <=0x57:
                    {
                        var value=Pop(stack,cil);var address=Pop(stack,cil);var size=cil.OpCode==0x52?1:cil.OpCode==0x53?2:cil.OpCode==0x55?8:4;instructions.Add(new ValueIrStoreIndirect(address,value,size,value.Kind==IrValueKind.ObjectReference));break;
                    }
                    case 0xfe15:
                    {
                        var type=resolveType(cil)??throw new NotSupportedException($"Unable to resolve initobj type at IL_{cil.Offset:x4}.");instructions.Add(new ValueIrInitValue(Pop(stack,cil),type));break;
                    }
                    case 0x70:
                    {
                        var type=resolveType(cil)??throw new NotSupportedException($"Unable to resolve cpobj type at IL_{cil.Offset:x4}.");var source=Pop(stack,cil);var destination=Pop(stack,cil);instructions.Add(new ValueIrCopyValue(destination,source,type));break;
                    }
                    case 0x71:
                    {
                        var type=resolveType(cil)??throw new NotSupportedException($"Unable to resolve ldobj type at IL_{cil.Offset:x4}.");var address=Pop(stack,cil);var result=New(ResultKind(analysis,cil));instructions.Add(new ValueIrLoadValue(result,address,type));stack.Add(result);break;
                    }
                    case 0x81:
                    {
                        var type=resolveType(cil)??throw new NotSupportedException($"Unable to resolve stobj type at IL_{cil.Offset:x4}.");var value=Pop(stack,cil);var address=Pop(stack,cil);instructions.Add(new ValueIrStoreValue(address,value,type));break;
                    }
                    case >=0x67 and <=0x6e or 0xd3 or 0xe0:
                    {
                        var input=Pop(stack,cil);var result=New(ResultKind(analysis,cil));instructions.Add(new ValueIrConvert(result,input));stack.Add(result);break;
                    }
                    case 0x25:
                    {
                        var value=Pop(stack,cil);stack.Add(value);stack.Add(value);break;
                    }
                    case 0x26:
                        Pop(stack,cil);break;
                    case 0xfe01 or 0xfe02 or 0xfe03 or 0xfe04 or 0xfe05:
                    {
                        var right=Pop(stack,cil);var left=Pop(stack,cil);var result=New(CilStackKind.I4);var op=cil.OpCode switch{0xfe01=>"ceq",0xfe02=>"cgt",0xfe03=>"cgt.un",0xfe04=>"clt",_=>"clt.un"};
                        instructions.Add(new ValueIrBinary(result,op,left,right));stack.Add(result);break;
                    }
                    case 0x58 or 0x59 or 0x5a or 0x5f:
                    {
                        var right=Pop(stack,cil);var left=Pop(stack,cil);var result=New(Merge(left.Kind,right.Kind));
                        instructions.Add(new ValueIrBinary(result,cil.OpCode switch{0x58=>"add",0x59=>"sub",0x5a=>"mul",_=>"and"},left,right));stack.Add(result);break;
                    }
                    case 0x74 or 0x75:
                    {
                        var type=resolveType(cil)??throw new NotSupportedException($"Unable to resolve type test at IL_{cil.Offset:x4}.");var obj=Pop(stack,cil);var result=New(CilStackKind.ObjectReference);instructions.Add(new ValueIrTypeTest(result,obj,type,cil.OpCode==0x74));stack.Add(result);break;
                    }
                    case 0x72:
                    {
                        var text=resolveString(cil)??throw new InvalidDataException($"Missing user string at IL_{cil.Offset:x4}.");var value=New(CilStackKind.ObjectReference);instructions.Add(new ValueIrLoadString(value,text));stack.Add(value);break;
                    }
                    case 0xfe06 or 0xfe07:
                    {
                        var target=resolveCall(cil)??throw new NotSupportedException($"Unresolved function pointer at IL_{cil.Offset:x4}.");IrValue? receiver=cil.OpCode==0xfe07?Pop(stack,cil):null;var result=New(CilStackKind.NativeInt);instructions.Add(new ValueIrFunctionPointer(result,target.Key,receiver));stack.Add(result);break;
                    }
                    case 0x28 or 0x6f:
                    {
                        var ik=intrinsic(cil);
                        if(ik==IntrinsicKind.StringLength){var str=Pop(stack,cil);var value=New(CilStackKind.I4);instructions.Add(new ValueIrStringLength(value,str));stack.Add(value);break;}
                        if(ik==IntrinsicKind.GameCubeWriteLine){instructions.Add(new ValueIrConsoleWriteLine(Pop(stack,cil)));break;}
                        if(ik==IntrinsicKind.GameCubeReadButtonsDown){var port=Pop(stack,cil);var value=New(CilStackKind.I4);instructions.Add(new ValueIrReadButtonsDown(value,port));stack.Add(value);break;}
                        if(ik==IntrinsicKind.GameCubePresentDemoFrame){instructions.Add(new ValueIrPresentDemoFrame(Pop(stack,cil)));break;}
                        if(ignoreCall(cil)){Pop(stack,cil);break;}
                        var target=resolveCall(cil);
                        if(target is null)
                        {
                            var effect=resolveCallEffect(cil)??throw new NotSupportedException($"Unresolved call at IL_{cil.Offset:x4}.");
                            for(var ai=effect.PopCount-1;ai>=0;ai--)Pop(stack,cil);
                            IrValue? externalResult=null;if(effect.PushKind is { } push){var value=new IrValue(nextValue++,Map(push));externalResult=value;stack.Add(value);}
                            instructions.Add(new ValueIrOpaqueStackEffect(effect.PopCount,externalResult is { } er?[er]:[],cil.OpCode));break;
                        }
                        var count=target.ParameterCount+(target.IsStatic?0:1);var args=new IrValue[count];
                        for(var ai=count-1;ai>=0;ai--)args[ai]=Pop(stack,cil);
                        if(delegateType(target.Key.TypeName)&&target.Key.Name=="Invoke")
                        {
                            if(target.ReturnsValue)throw new NotSupportedException("Value-returning delegates are not yet supported.");
                            instructions.Add(new ValueIrDelegateInvoke(args[0],target.ParameterCount==1?args[1]:null));break;
                        }
                        IrValue? result=null;if(target.ReturnsValue){var value=New(ResultKind(analysis,cil));result=value;stack.Add(value);}
                        var isInterface=cil.OpCode==0x6f&&resolveTypeForMethod(target.Key.TypeName); instructions.Add(new ValueIrCall(result,target.Key,args,cil.OpCode==0x6f&&target.IsVirtual&&!isInterface,isInterface));break;
                    }
                    case 0x73:
                    {
                        var target=resolveCall(cil)??throw new NotSupportedException($"Unresolved constructor at IL_{cil.Offset:x4}.");var args=new IrValue[target.ParameterCount];for(var ai=args.Length-1;ai>=0;ai--)args[ai]=Pop(stack,cil);var value=New(CilStackKind.ObjectReference);if(delegateType(target.Key.TypeName)&&args.Length==2)instructions.Add(new ValueIrNewDelegate(value,target.Key.TypeName,args[0],args[1]));else instructions.Add(new ValueIrNewObject(value,target.Key.TypeName,target.Key,args));stack.Add(value);break;
                    }
                    case 0x7b:
                    {
                        var field=resolveField(cil)??throw new NotSupportedException($"Unresolved field at IL_{cil.Offset:x4}.");var obj=Pop(stack,cil);var value=New(field.IsReference?CilStackKind.ObjectReference:ResultKind(analysis,cil));instructions.Add(new ValueIrLoadField(value,obj,field.DeclaringType,field.Name));stack.Add(value);break;
                    }
                    case 0x7d:
                    {
                        var field=resolveField(cil)??throw new NotSupportedException($"Unresolved field at IL_{cil.Offset:x4}.");var value=Pop(stack,cil);var obj=Pop(stack,cil);instructions.Add(new ValueIrStoreField(obj,value,field.DeclaringType,field.Name));break;
                    }
                    case 0x7e:
                    {
                        var field=resolveField(cil)??throw new NotSupportedException($"Unresolved static field at IL_{cil.Offset:x4}.");var value=New(field.IsReference?CilStackKind.ObjectReference:ResultKind(analysis,cil));instructions.Add(new ValueIrLoadStaticField(value,field.DeclaringType,field.Name));stack.Add(value);break;
                    }
                    case 0x80:
                    {
                        var field=resolveField(cil)??throw new NotSupportedException($"Unresolved static field at IL_{cil.Offset:x4}.");instructions.Add(new ValueIrStoreStaticField(Pop(stack,cil),field.DeclaringType,field.Name));break;
                    }
                    case 0x2b or 0x38:
                        terminator=new ValueIrJump(Target(blocks,cil));break;
                    case 0x2c or 0x39:
                    {
                        var condition=Pop(stack,cil);var target=Target(blocks,cil);terminator=new ValueIrBranch(condition,null,ValueIrComparison.NonZero,false,Fallthrough(blocks,block,cil),target);break;
                    }
                    case 0x2d or 0x3a:
                    {
                        var condition=Pop(stack,cil);var target=Target(blocks,cil);terminator=new ValueIrBranch(condition,null,ValueIrComparison.NonZero,false,target,Fallthrough(blocks,block,cil));break;
                    }
                    case >=0x2e and <=0x37 or >=0x3b and <=0x44:
                    {
                        var right=Pop(stack,cil);var left=Pop(stack,cil);var target=Target(blocks,cil);var fall=Fallthrough(blocks,block,cil);
                        var shortForm=cil.OpCode<=0x37;var n=shortForm?cil.OpCode-0x2e:cil.OpCode-0x3b;
                        var cmp=n switch{0=>ValueIrComparison.Equal,1 or 6=>ValueIrComparison.GreaterOrEqual,2 or 7=>ValueIrComparison.GreaterThan,3 or 8=>ValueIrComparison.LessOrEqual,4 or 9=>ValueIrComparison.LessThan,5=>ValueIrComparison.NotEqual,_=>throw new InvalidDataException($"Invalid relational branch opcode 0x{cil.OpCode:x4}.")};
                        var unsigned=n>=5;
                        terminator=new ValueIrBranch(left,right,cmp,unsigned,target,fall);break;
                    }
                    case 0x45:
                    {
                        var value=Pop(stack,cil);var sw=cil.Operand as CilSwitchTargets??throw new InvalidDataException($"Missing switch targets at IL_{cil.Offset:x4}.");
                        var targets=sw.Offsets.Select(offset=>blocks.Single(x=>x.StartOffset==offset).Id).ToArray();var fallback=blocks.Single(x=>x.StartOffset==cil.EndOffset).Id;
                        terminator=new ValueIrSwitch(value,targets,fallback);break;
                    }
                    case 0x7a: terminator=new ValueIrThrow(Pop(stack,cil));break;
                    case 0x2a:
                        terminator=new ValueIrReturn(method.ReturnsValue?Pop(stack,cil):null);break;
                    default:
                    {
                        // Keep stack identities consistent for operations whose semantic lowering
                        // has not moved to Value IR yet. Stack analysis is the source of truth.
                        var expected=analysis.InstructionExitStates.TryGetValue(cil.Offset,out var validated)?validated.Values.Count:throw new InvalidDataException($"Missing analyzed stack state at IL_{cil.Offset:x4}.");
                        while(stack.Count>expected)Pop(stack,cil);
                        var results=new List<IrValue>();
                        while(stack.Count<expected){var v=New(ResultKind(analysis,cil,stack.Count));stack.Add(v);results.Add(v);}
                        instructions.Add(new ValueIrOpaqueStackEffect(Math.Max(0,beforeCount-expected),results,cil.OpCode));
                        break;
                    }
                }
            }
            if(terminator is null&&block.Successors.Count==1)terminator=new ValueIrJump(block.Successors[0]);
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
            var predecessors=blocks[block.Id].Predecessors.Select(id=>blocks[id]).ToArray();
            if(predecessors.Length==0)continue;
            for(var slot=0;slot<block.EntryStack.Values.Count;slot++)
            {
                var inputs=new Dictionary<int,IrValue>();
                foreach(var pred in predecessors)
                    if(exitValues.TryGetValue(pred.Id,out var values)&&slot<values.Count)inputs[pred.Id]=values[slot];
                if(inputs.Count!=predecessors.Length)throw new InvalidDataException($"Incomplete Value IR edge state for block {block.Id}, stack slot {slot}.");
                // A single predecessor still needs an explicit edge assignment because entry
                // identities are deliberately preallocated and independent of traversal order.
                block.Instructions.Insert(slot,new ValueIrPhi(block.EntryStack.Values[slot],inputs));
            }
        }
        var locals=InferLocals(output,localCount);
        return new ValueIrMethod(method.Key,output,locals,method.ParameterCount,!method.IsStatic,method.ReturnsValue);
    }

    private static IReadOnlyList<ValueIrLocal> InferLocals(IReadOnlyList<ValueIrBlock> blocks,int count)
    {
        var kinds=Enumerable.Repeat(IrValueKind.Unknown,count).ToArray();
        foreach(var instruction in blocks.SelectMany(b=>b.Instructions))
        {
            if(instruction is ValueIrStoreLocal store&&store.Index<count)kinds[store.Index]=MergeLocal(kinds[store.Index],store.Value.Kind);
            else if(instruction is ValueIrLoadLocal load&&load.Index<count)kinds[load.Index]=MergeLocal(kinds[load.Index],load.Result.Kind);
        }
        return kinds.Select((kind,index)=>new ValueIrLocal(index,kind)).ToArray();
    }
    private static bool IsReferenceType(string type)=>type is "System.String" or "System.Object" || !type.StartsWith("System.",StringComparison.Ordinal);
    private static uint ElementSize(string type)=>type switch{"System.Boolean" or "System.Byte" or "System.SByte"=>1u,"System.Char" or "System.Int16" or "System.UInt16"=>2u,"System.Int64" or "System.UInt64" or "System.Double"=>8u,_=>4u};

    private static IrValueKind MergeLocal(IrValueKind current,IrValueKind next)=>current==IrValueKind.Unknown?next:next==IrValueKind.Unknown||current==next?current:IrValueKind.Unknown;

    private static CilStackKind ResultKind(CilStackAnalysis analysis,CilInstruction instruction,int index=-1)
    {
        if(!analysis.InstructionExitStates.TryGetValue(instruction.Offset,out var state)||state.Values.Count==0)return CilStackKind.Unknown;
        var i=index>=0?index:state.Values.Count-1;
        return i>=0&&i<state.Values.Count?state.Values[i]:CilStackKind.Unknown;
    }

    private static IrValue Pop(List<IrValue>s,CilInstruction i){if(s.Count==0)throw new InvalidDataException($"Value IR stack underflow at IL_{i.Offset:x4}.");var v=s[^1];s.RemoveAt(s.Count-1);return v;}
    private static int Target(IReadOnlyList<CilBasicBlock>b,CilInstruction i){var offset=(i.Operand as CilBranchTarget)?.Offset??throw new InvalidDataException("Missing branch target.");return b.Single(x=>x.StartOffset==offset).Id;}
    private static int Fallthrough(IReadOnlyList<CilBasicBlock>b,CilBasicBlock current,CilInstruction i)=>b.Single(x=>x.StartOffset==i.EndOffset).Id;
    private static CilStackKind Merge(IrValueKind a,IrValueKind b)=>a==b?Unmap(a):CilStackKind.Unknown;
    private static IrValueKind Map(CilStackKind k)=>k switch{CilStackKind.I4=>IrValueKind.I4,CilStackKind.I8=>IrValueKind.I8,CilStackKind.NativeInt=>IrValueKind.NativeInt,CilStackKind.Float=>IrValueKind.R8,CilStackKind.ObjectReference=>IrValueKind.ObjectReference,CilStackKind.ManagedPointer=>IrValueKind.ManagedPointer,_=>IrValueKind.Unknown};
    private static CilStackKind Unmap(IrValueKind k)=>k switch{IrValueKind.I4=>CilStackKind.I4,IrValueKind.I8=>CilStackKind.I8,IrValueKind.NativeInt=>CilStackKind.NativeInt,IrValueKind.R4 or IrValueKind.R8=>CilStackKind.Float,IrValueKind.ObjectReference=>CilStackKind.ObjectReference,IrValueKind.ManagedPointer=>CilStackKind.ManagedPointer,_=>CilStackKind.Unknown};
}
