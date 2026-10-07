using System;
using System.Collections.Generic;
using DolphinDotNet.Compatibility;

namespace DolphinDotNet.Core.Tests;

internal readonly struct ComparableValue:IEquatable<ComparableValue>,IComparable<ComparableValue>
{
    public ComparableValue(int value)=>Value=value;
    public int Value{get;}
    public bool Equals(ComparableValue other)=>Value==other.Value;
    public override bool Equals(object? obj)=>obj is ComparableValue other&&Equals(other);
    public override int GetHashCode()=>Value*17;
    public int CompareTo(ComparableValue other)=>Value.CompareTo(other.Value);
}

internal static class Program
{
    private static int Main()
    {
        var list=new CompatList<int>();list.Add(2);list.Add(3);list.Add(4);
        if(list.Count!=3||!list.Contains(3)||list.Contains(9))return 1;
        if(!list.Remove(3)||list.Count!=2)return 2;
        var dict=new CompatDictionary<string,int>();dict.Add("a",1);dict["b"]=2;
        if(!dict.TryGetValue("b",out var two)||two!=2||dict.ContainsKey("c"))return 3;
        if(CompatLinq.Count(CompatLinq.Where(list,x=>x%2==0))!=2)return 4;
        if(CompatLinq.First(CompatLinq.Select(list,x=>x+1))!=3)return 5;
        if(Foundation.Concat("a","b")!="ab"||Foundation.IndexOf("gamecube","cube")!=4)return 6;
        int? nullable=7;if(Foundation.NullableOrDefault(nullable)!=7)return 7;
        var values=new[]{1,2,3};var copy=new int[3];Foundation.Copy(values,copy,3);if(copy[2]!=3)return 8;
        Foundation.Clear(copy);if(copy[0]!=0||copy[2]!=0)return 9;
        var eq=CompatEqualityComparer<ComparableValue>.Default;if(!eq.Equals(new ComparableValue(4),new ComparableValue(4))||eq.Equals(new ComparableValue(4),new ComparableValue(5)))return 10;
        if(eq.GetHashCode(new ComparableValue(3))!=51)return 11;
        var cmp=CompatComparer<ComparableValue>.Default;if(cmp.Compare(new ComparableValue(2),new ComparableValue(5))>=0||cmp.Compare(new ComparableValue(5),new ComparableValue(2))<=0||cmp.Compare(new ComparableValue(3),new ComparableValue(3))!=0)return 12;
        var stringEq=CompatEqualityComparer<string>.Default;if(!stringEq.Equals("cube","cube")||stringEq.Equals("cube",null)||stringEq.GetHashCode(null!)!=0)return 13;
        var stringCmp=CompatComparer<string>.Default;if(stringCmp.Compare(null,"a")>=0||stringCmp.Compare("a",null)<=0||stringCmp.Compare("a","b")>=0)return 14;
        if(Foundation.Hash(new ComparableValue(2))!=34||Foundation.Compare(new ComparableValue(2),new ComparableValue(7))>=0)return 15;
        return 0;
    }
}
