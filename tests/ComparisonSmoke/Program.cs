using System;
using DolphinDotNet.Compatibility;

internal readonly struct Score:IEquatable<Score>,IComparable<Score>
{
    public Score(int value)=>Value=value;
    public int Value{get;}
    public bool Equals(Score other)=>Value==other.Value;
    public override bool Equals(object? obj)=>obj is Score other&&Equals(other);
    public override int GetHashCode()=>Value*31;
    public int CompareTo(Score other)=>Value-other.Value;
}

public static class Program
{
    public static int Main()
    {
        var eq=CompatEqualityComparer<Score>.Default;
        var cmp=CompatComparer<Score>.Default;
        var score=0;
        if(eq.Equals(new Score(4),new Score(4)))score+=1;
        if(!eq.Equals(new Score(4),new Score(5)))score+=2;
        if(eq.GetHashCode(new Score(3))==93)score+=4;
        if(cmp.Compare(new Score(2),new Score(5))<0)score+=8;
        if(cmp.Compare(new Score(5),new Score(2))>0)score+=16;
        if(cmp.Compare(new Score(3),new Score(3))==0)score+=32;
        return score==63?0:score;
    }
}
