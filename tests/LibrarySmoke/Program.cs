using System;
using System.Collections.Generic;
internal static class Program
{
    private static int GenericHash<T>(T value)=>value!.GetHashCode();
    private static bool GenericEquals<T>(T value,object other)=>value!.Equals(other);
    private static int asyncValue;
    private static async System.Threading.Tasks.Task<int> AsyncProbe(){await System.Threading.Tasks.Task.Yield();asyncValue=17;var value=await System.Threading.Tasks.Task.Run(()=>25).ConfigureAwait(false);return value+asyncValue;}
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
        var faulted=System.Threading.Tasks.Task.Run(()=>{throw new InvalidOperationException("async fault");});
        try{faulted.GetAwaiter().GetResult();return 37;}catch(InvalidOperationException){}if(!faulted.IsFaulted)return 38;
        var copiedMemory=new System.IO.MemoryStream();textMemory.Position=0;var copyTask=textMemory.CopyToAsync(copiedMemory,2);if(copyTask.IsCompleted)return 39;copyTask.GetAwaiter().GetResult();if(copiedMemory.Length!=textMemory.Length)return 40;
        try{new System.Text.UTF8Encoding(false,true).GetString(new byte[]{0xff});return 41;}catch(System.Text.DecoderFallbackException){}
        if(EqualityComparer<object>.Default.Equals(new Item(1),new Item(1)))return 42;
        if(!StringComparer.OrdinalIgnoreCase.Equals("\u03c2","\u03a3")||StringComparer.OrdinalIgnoreCase.GetHashCode("aBC")!=StringComparer.OrdinalIgnoreCase.GetHashCode("Abc"))return 43;
        if(StringComparer.Ordinal.Equals("a","A")||StringComparer.Ordinal.Compare("a","b")>=0)return 44;
        object boxedPair=new Pair(9,"boxed");if(!boxedPair.Equals(new Pair(9,"boxed"))||boxedPair.GetHashCode()!=9)return 45;
        var overrideObject=new OverrideEquals(2);if(!EqualityComparer<object>.Default.Equals(overrideObject,new OverrideEquals(2))||overrideObject.GetHashCode()!=2)return 46;
        if(GenericHash(new Pair(7,"hash"))!=7||!GenericEquals(new Pair(7,"eq"),new Pair(7,"eq")))return 47;
        if(GenericHash("text")!=EqualityComparer<string>.Default.GetHashCode("text"))return 48;
        var doubles=new List<double>();doubles.Add(0.6);doubles.Add(0.5);doubles.Sort();if(doubles[0]!=0.5||!doubles.Contains(0.6))return 49;
        object doubleBox=0.5;if(!doubleBox.Equals((object)0.5)||doubleBox.Equals((object)0.6)||(double)doubleBox!=0.5)return 50;
        IEnumerable<double> doubleEnumerable=doubles;var doubleSum=0.0;foreach(var value in doubleEnumerable)doubleSum+=value;if(doubleSum!=1.1)return 51;
        if(!EqualityComparer<GenericBox<long>>.Default.Equals(new GenericBox<long>(0x100000003L),new GenericBox<long>(0x100000003L)))return 52;
        var genericStructs=new List<GenericBox<string>>();genericStructs.Add(new GenericBox<string>("generic"));if(!genericStructs.Contains(new GenericBox<string>("generic")))return 53;
        var allA=System.Threading.Tasks.Task.Run(()=>asyncValue=1);var allB=System.Threading.Tasks.Task.Run(()=>asyncValue=2);var all=System.Threading.Tasks.Task.WhenAll(new System.Threading.Tasks.Task[]{allA,allB});if(all.IsCompleted)return 54;all.GetAwaiter().GetResult();if(!allA.IsCompleted||!allB.IsCompleted)return 55;
        var byteList=new List<byte>();byteList.Add(255);if(byteList[0]!=255)return 56;
        object signedBox=(sbyte)-1;if((sbyte)signedBox!=-1)return 57;
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

internal sealed class OverrideEquals {private readonly int n;public OverrideEquals(int n){this.n=n;}public override bool Equals(object? other)=>other is OverrideEquals item&&n==item.n;public override int GetHashCode()=>n;}

internal struct GenericBox<T>{public T Value;public GenericBox(T value){Value=value;}}
