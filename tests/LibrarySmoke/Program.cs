using System;
using System.Collections.Generic;
internal static class Program
{
    private static int asyncValue;
    private static async System.Threading.Tasks.Task<int> AsyncProbe(){await System.Threading.Tasks.Task.Yield();asyncValue=17;var value=await System.Threading.Tasks.Task.Run(()=>25);return value+asyncValue;}
    private static bool IsThree(int value)=>value==3;
    private static int Descending(int x,int y)=>y-x;
    private static int Main()
    {
        if(!EqualityComparer<int>.Default.Equals(7,7)||EqualityComparer<int>.Default.Equals(7,8))return 1;
        if(Comparer<uint>.Default.Compare(0xffffffffu,1u)<=0)return 2;
        if(Comparer<long>.Default.Compare(0x100000002L,2L)<=0)return 3;
        if(!EqualityComparer<string>.Default.Equals(string.Concat("ab","cd"),"abcd"))return 4;
        if(EqualityComparer<string>.Default.GetHashCode("abcd")!=EqualityComparer<string>.Default.GetHashCode(string.Concat("ab","cd")))return 5;
        if(Comparer<string>.Default.Compare(null!,"a")>=0||Comparer<string>.Default.Compare("ab","ac")>=0)return 6;
        int? empty=null;int? present=7;
        if(!Nullable.Equals(empty,empty)||Nullable.Equals(empty,present)||Nullable.Compare(empty,present)>=0)return 7;
        if(!EqualityComparer<int?>.Default.Equals(present,7)||EqualityComparer<int?>.Default.GetHashCode(empty)!=0)return 8;
        var item=new Item(3);
        if(!EqualityComparer<Item>.Default.Equals(item,new Item(3))||Comparer<Item>.Default.Compare(item,new Item(5))>=0)return 9;
        if(EqualityComparer<Item>.Default.GetHashCode(item)!=3)return 10;
        try { Comparer<object>.Default.Compare(new Plain(),new Plain());return 11; }catch(ArgumentException){}
        var pair=new Pair(3,"abc");
        if(!EqualityComparer<Pair>.Default.Equals(pair,new Pair(3,string.Concat("a","bc"))))return 12;
        if(EqualityComparer<Pair>.Default.Equals(pair,new Pair(4,"abc")))return 13;
        var list=new List<int>();list.Add(3);list.Add(1);list.Add(2);list.Sort();
        if(list.Count!=3||list[0]!=1||list.BinarySearch(2)!=1)return 14;
        var sum=0;foreach(var n in list)sum+=n;if(sum!=6)return 15;
        var enumerator=list.GetEnumerator();if(!enumerator.MoveNext()||enumerator.Current!=1)return 16;
        var copied=enumerator;if(!copied.MoveNext()||copied.Current!=2||enumerator.Current!=1)return 17;
        IEnumerable<int> enumerable=list;sum=0;foreach(var n in enumerable)sum+=n;if(sum!=6)return 18;
        var structs=new List<Pair>();structs.Add(pair);if(!structs.Contains(new Pair(3,"abc")))return 19;
        var fromArray=new List<int>(new[]{4,2,3});fromArray.AddRange(fromArray);
        if(fromArray.Count!=6||fromArray[3]!=4)return 20;
        fromArray.RemoveRange(1,2);if(fromArray.Count!=4)return 21;
        if(fromArray.FindIndex(IsThree)!=3)return 22;
        fromArray.Sort(Descending);if(fromArray[0]!=4)return 23;
        var memory=new System.IO.MemoryStream();memory.WriteByte(65);memory.Position=4;memory.WriteByte(66);
        if(memory.Length!=5||memory.ToArray()[2]!=0)return 24;
        memory.Position=0;if(memory.ReadByte()!=65)return 25;
        var encoding=new System.Text.UTF8Encoding(false,true);
        var encoded=encoding.GetBytes("a\u20ac\ud83d\ude00");
        if(encoded.Length!=8||encoding.GetString(encoded)!="a\u20ac\ud83d\ude00")return 26;
        var textMemory=new System.IO.MemoryStream();
        var writer=new System.IO.StreamWriter(textMemory,new System.Text.UTF8Encoding(true),2,true);
        writer.Write("one\r\ntwo\n\ud83d\ude00");writer.Dispose();textMemory.Position=0;
        var reader=new System.IO.StreamReader(textMemory,System.Text.Encoding.UTF8,true,2,true);
        if(reader.ReadLine()!="one"||reader.ReadLine()!="two"||reader.ReadToEnd()!="\ud83d\ude00")return 27;
        reader.Dispose();if(!textMemory.CanRead)return 28;
        var task=AsyncProbe();if(task.IsCompleted||asyncValue!=0)return 29;
        if(task.GetAwaiter().GetResult()!=42||asyncValue!=17)return 30;
        if(Comparer<double>.Default.Compare(-1.5,1.0)>=0||!EqualityComparer<double>.Default.Equals(double.NaN,double.NaN))return 31;
        if(EqualityComparer<double>.Default.GetHashCode(-0.0)!=EqualityComparer<double>.Default.GetHashCode(0.0))return 32;
        if(EqualityComparer<double>.Default.Equals(0.5,0.6)||Comparer<double>.Default.Compare(0.5,0.6)>=0)return 33;
        if(!EqualityComparer<Small>.Default.Equals(new Small(1,2),new Small(1,2))||EqualityComparer<Small>.Default.Equals(new Small(1,2),new Small(1,3)))return 34;
        if(!EqualityComparer<Nested>.Default.Equals(new Nested(new Pair(4,"x")),new Nested(new Pair(4,"x"))))return 35;
        if(Comparer<Unsigned>.Default.Compare(Unsigned.Large,Unsigned.Small)<=0)return 36;
        return 0;
    }
}
internal sealed class Item : IEquatable<Item>,IComparable<Item>
{
    private readonly int value;
    public Item(int value){this.value=value;}
    public bool Equals(Item? other)=>other!=null&&value==other.value;
    public int CompareTo(Item? other)=>other==null?1:value-other.value;
    public override int GetHashCode()=>value;
}

internal sealed class Plain {}

internal struct Pair : IEquatable<Pair> { public int Number;public string Text; public Pair(int n,string t){Number=n;Text=t;} public bool Equals(Pair other)=>Number==other.Number&&Text==other.Text;public override int GetHashCode()=>Number; }

internal struct Small {public byte A;public short B;public Small(byte a,short b){A=a;B=b;}}
internal struct Nested {public Pair Pair;public Nested(Pair pair){Pair=pair;}}
internal enum Unsigned:uint {Small=1,Large=0xffffffffu}
