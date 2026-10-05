using System;

public static class Program
{
    public static int Main()
    {
        var loop=LoopSum();
        var branches=BranchCases(7);
        var nested=Nested(3,8);
        var shortCircuit=ShortCircuit(4,9);
        var mutated=MutateArgument(2);
        var switched=SwitchCase(4);
        var overloaded=Overload(7)+Overload(5,6);
        return loop+branches+nested+shortCircuit+mutated+switched+overloaded;
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
