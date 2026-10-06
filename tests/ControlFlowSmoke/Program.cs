using System;

delegate void IntSink(int value);
delegate int IntFn(int value);
interface IValue { int GetValue(); }
class VirtualBase { public int BaseField=4; public virtual int GetValue()=>3; }
class VirtualDerived : VirtualBase, IValue { public int DerivedField=5; public override int GetValue()=>9; }
class SmokeException : Exception { }

public static class Program
{
    static int StaticValue=4;
    static int DelegateTotal;
    public static int Main()
    {
        var loop=LoopSum();
        var branches=BranchCases(7);
        var nested=Nested(3,8);
        var shortCircuit=ShortCircuit(4,9);
        var mutated=MutateArgument(2);
        var switched=SwitchCase(4);
        var overloaded=Overload(7)+Overload(5,6);
        var arrays=ArrayCase();
        var statics=StaticCase();
        var generic=Identity(6);
        var boxing=BoxCase();
        var virtuals=VirtualCase();
        var types=TypeCase();
        var inherited=InheritedFieldCase();
        var byref=ByRefCase();
        var iface=InterfaceIdentityCase();
        var ifaceCall=InterfaceCallCase();
        var delegates=DelegateCase()+ReturningDelegateCase();
        var exceptions=ExceptionCase()+FinallyCase()+NestedFinallyCase()+RethrowCase()+CatchThrowsCase()+ReturnFinallyCase()+RethrowIdentityCase()+TypedCatchCase();
        var core=CorePrimitiveCase();
        return loop+branches+nested+shortCircuit+mutated+switched+overloaded+arrays+statics+generic+boxing+virtuals+types+inherited+byref+iface+ifaceCall+delegates+exceptions+core;
    }

    static int CorePrimitiveCase()=>ObjectPrimitiveCase()+StringPrimitiveCase()+ArrayPrimitiveCase();

    static int ObjectPrimitiveCase()
    {
        object same=new VirtualDerived();
        var score=object.ReferenceEquals(same,same)?1:0;
        if(!object.ReferenceEquals(same.GetType(),null))score+=2;
        if(same.GetHashCode()==same.GetHashCode())score+=4;
        if(same.Equals(same))score+=8;
        if(same.ToString().Length>0)score+=16;
        return score;
    }

    static int StringPrimitiveCase()
    {
        var text="hello";
        var score=text.IndexOf("ell")+text.Substring(1,3).Length;
        if(text[1]=='e')score+=4;
        if(text.StartsWith("he"))score+=8;
        if(text.EndsWith("lo"))score+=16;
        if(text.Contains("ell"))score+=32;
        if(("he"+"llo")==text)score+=64;
        return score;
    }

    static int ArrayPrimitiveCase()
    {
        var source=new int[3];source[0]=1;source[1]=2;source[2]=3;var destination=new int[3];
        Array.Copy(source,destination,3);Array.Clear(destination,1,1);
        var score=destination[0]+destination[1]+destination[2];
        if(source.Rank==1)score+=4;
        if(source.GetLength(0)==3)score+=8;
        foreach(var value in source)score+=value;
        return score;
    }

    static int ExceptionCase(){try{ThrowHelper();return 0;}catch(Exception){return 7;}}
    static void ThrowHelper(){throw new Exception();}
    static int FinallyCase(){var value=1;try{value=2;}finally{value=value+3;}return value;}
    static int NestedFinallyCase(){var value=0;try{try{value=1;ThrowHelper();}finally{value=value+2;}}catch(Exception){value=value+4;}return value;}
    static int RethrowCase(){try{try{ThrowHelper();}catch(Exception){throw;}}catch(Exception){return 11;}return 0;}
    static int CatchThrowsCase(){try{try{ThrowHelper();}catch(Exception){throw new Exception();}}catch(Exception){return 13;}return 0;}
    static int ReturnFinallyCase(){FinallyProbe=0;var result=ReturnInsideTry();return result+FinallyProbe;}
    static int ReturnInsideTry(){try{return 17;}finally{FinallyProbe=5;}}
    static int RethrowIdentityCase(){Exception? captured=null;try{try{ThrowHelper();}catch(Exception ex){captured=ex;throw;}}catch(Exception ex){return object.ReferenceEquals(captured,ex)?19:0;}return 0;}
    static int TypedCatchCase(){try{throw new SmokeException();}catch(SmokeException){return 23;}catch(Exception){return 0;}}


    static int DelegateCase(){IntSink sink=Sink;sink(6);return DelegateTotal;}
    static int ReturningDelegateCase(){var fn=new IntFn(AddOne);return fn(10);}
    static int AddOne(int value)=>value+1;
    static void Sink(int value){DelegateTotal=DelegateTotal+value;}

    static int InterfaceCallCase(){IValue value=new VirtualDerived();return value.GetValue();}

    static int InterfaceIdentityCase(){object value=new VirtualDerived();return value is IValue?4:0;}

    static int ByRefCase(){var value=2;AddThree(ref value);return value;}
    static void AddThree(ref int value){value=5;}

    static int InheritedFieldCase(){var value=new VirtualDerived();return value.BaseField+value.DerivedField;}

    static int TypeCase(){VirtualBase value=new VirtualDerived();var derived=value as VirtualDerived;var cast=(VirtualDerived)value;return (derived!=null?2:0)+(cast!=null?3:0);}

    static int VirtualCase(){VirtualBase value=new VirtualDerived();return value.GetValue();}

    static int BoxCase(){object value=7;return (int)value;}

    static T Identity<T>(T value)=>value;

    static int StaticCase()=>StaticValue;

    static int ArrayCase()
    {
        var values=new int[3];
        values[0]=1;values[1]=2;values[2]=3;
        return values[0]+values[1]+values[2]+values.Length-3;
    }

    static int Overload(int value)=>value;
    static int Overload(int left,int right)=>left+right;

    static int SwitchCase(int value)
    {
        switch(value)
        {
            case 0:return 10;
            case 1:return 20;
            case 2:return 30;
            case 3:return 35;
            case 4:return 40;
            case 5:return 50;
            default:return 60;
        }
    }

    static int MutateArgument(int value){value=value+3;return value;}

    static int LoopSum()
    {
        var i=0;var sum=0;
        while(i<5){sum=sum+i;i=i+1;}
        return sum;
    }

    static int BranchCases(int value)
    {
        var a=value>5?10:20;
        if(value==7)a=a+3;
        else a=a-3;
        return a;
    }

    static int Nested(int a,int b)
    {
        if(a<b)
        {
            if(b>=8)return 5;
            return 6;
        }
        return 7;
    }

    static int ShortCircuit(int a,int b)
    {
        var score=0;
        if(a>0&&b>0)score=score+11;
        if(a<0||b==9)score=score+13;
        return score;
    }
}
