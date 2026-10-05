using System;

interface IValue { int GetValue(); }
class VirtualBase { public int BaseField=4; public virtual int GetValue()=>3; }
class VirtualDerived : VirtualBase, IValue { public int DerivedField=5; public override int GetValue()=>9; }

public static class Program
{
    static int StaticValue=4;
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
        var boxing=BoxCase()+BoxShortCase();
        var virtuals=VirtualCase();
        var types=TypeCase();
        var inherited=InheritedFieldCase();
        var byref=ByRefCase();
        var iface=InterfaceIdentityCase();
        return loop+branches+nested+shortCircuit+mutated+switched+overloaded+arrays+statics+generic+boxing+virtuals+types+inherited+byref+iface;
    }

    static int InterfaceIdentityCase(){object value=new VirtualDerived();return (value is IValue?4:0)+((IValue)value).GetValue();}

    static int ByRefCase(){var value=2;AddThree(ref value);return value;}
    static void AddThree(ref int value){value=5;}

    static int InheritedFieldCase(){var value=new VirtualDerived();return value.BaseField+value.DerivedField;}

    static int TypeCase(){VirtualBase value=new VirtualDerived();var derived=value as VirtualDerived;var cast=(VirtualDerived)value;return (derived!=null?2:0)+(cast!=null?3:0);}

    static int VirtualCase(){VirtualBase value=new VirtualDerived();return value.GetValue();}

    static int BoxShortCase(){object value=(short)8;return (short)value;}

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
