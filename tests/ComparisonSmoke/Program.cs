using System;
using DolphinDotNet.Compatibility;

internal readonly struct Score:ICompatEquatable<Score>,ICompatComparable<Score>
{
    public Score(int value)=>Value=value;
    public int Value{get;}
    public bool Equals(Score other)=>Value==other.Value;
    public override bool Equals(object? obj)=>obj is Score other&&Equals(other);
    public override int GetHashCode()=>Value*31;
    public int GetCompatHashCode()=>Value*31;
    public int CompareTo(Score other)=>Value-other.Value;
}

public static class Program
{
    public static int EqualityCase()
    {
        var eq=CompatEqualityComparer<Score>.Default;
        var score=0;
        if(eq.Equals(new Score(4),new Score(4)))score+=1;
        if(!eq.Equals(new Score(4),new Score(5)))score+=2;
        return score;
    }

    public static int HashCase()
    {
        var eq=CompatEqualityComparer<Score>.Default;
        return eq.GetHashCode(new Score(3))==93?4:0;
    }

    public static int CompareCase()
    {
        var cmp=CompatComparer<Score>.Default;
        var score=0;
        if(cmp.Compare(new Score(2),new Score(5))<0)score+=8;
        if(cmp.Compare(new Score(5),new Score(2))>0)score+=16;
        if(cmp.Compare(new Score(3),new Score(3))==0)score+=32;
        return score;
    }

    public static int Main()
    {
        var score=EqualityCase()+HashCase()+CompareCase();
        return score==63?0:score;
    }
}
