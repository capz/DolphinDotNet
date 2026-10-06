using System;
using System.Collections.Generic;
using DolphinDotNet.Compatibility;

namespace DolphinDotNet.Core.Tests;

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
        return 0;
    }
}
