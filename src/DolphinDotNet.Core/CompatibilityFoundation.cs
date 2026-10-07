using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace DolphinDotNet.Compatibility;

public static class Foundation
{
    public static bool ReferenceEquals(object? a,object? b)=>object.ReferenceEquals(a,b);
    public static bool Equals(object? a,object? b)=>object.Equals(a,b);
    public static int Hash<T>(T value)=>CompatEqualityComparer<T>.Default.GetHashCode(value!);
    public static int Compare<T>(T a,T b)=>CompatComparer<T>.Default.Compare(a,b);
    public static T? NullableOrDefault<T>(T? value) where T:struct=>value;
    public static string Concat(string? a,string? b)=>string.Concat(a,b);
    public static int IndexOf(string value,string needle)=>value.IndexOf(needle,StringComparison.Ordinal);
    public static string BuildString(string a,string b){var s=new StringBuilder();s.Append(a);s.Append(b);return s.ToString();}
    public static void Clear<T>(T[] values)=>Array.Clear(values,0,values.Length);
    public static void Copy<T>(T[] source,T[] target,int count)=>Array.Copy(source,target,count);
}

public sealed class CompatList<T>:IEnumerable<T>,IReadOnlyList<T>
{
    private T[] items; public int Count{get;private set;}
    public CompatList(int capacity=4)=>items=new T[capacity<1?1:capacity];
    public T this[int index]{get{Check(index);return items[index];}set{Check(index);items[index]=value;}}
    public void Add(T value){Ensure(Count+1);items[Count++]=value;}
    public bool Contains(T value){var eq=CompatEqualityComparer<T>.Default;for(var i=0;i<Count;i++)if(eq.Equals(items[i],value))return true;return false;}
    public bool Remove(T value){var eq=CompatEqualityComparer<T>.Default;for(var i=0;i<Count;i++)if(eq.Equals(items[i],value)){for(var j=i+1;j<Count;j++)items[j-1]=items[j];items[--Count]=default!;return true;}return false;}
    public void Clear(){Array.Clear(items,0,Count);Count=0;}
    public Enumerator GetEnumerator()=>new(this);
    IEnumerator<T> IEnumerable<T>.GetEnumerator()=>new Enumerator(this);
    IEnumerator IEnumerable.GetEnumerator()=>new Enumerator(this);
    private void Check(int index){if((uint)index>=(uint)Count)throw new ArgumentOutOfRangeException(nameof(index));}
    private void Ensure(int count){if(count<=items.Length)return;var next=new T[items.Length*2];Array.Copy(items,next,Count);items=next;}

    public struct Enumerator:IEnumerator<T>
    {
        private readonly CompatList<T> list; private int index;
        internal Enumerator(CompatList<T> list){this.list=list;index=-1;}
        public T Current=>list.items[index];
        object? IEnumerator.Current=>Current;
        public bool MoveNext(){var next=index+1;if(next>=list.Count)return false;index=next;return true;}
        public void Reset()=>index=-1;
        public void Dispose(){}
    }
}

public sealed class CompatDictionary<TKey,TValue> where TKey:notnull
{
    private TKey[] keys=new TKey[4]; private TValue[] values=new TValue[4]; private bool[] used=new bool[4];
    public int Count{get;private set;}
    public TValue this[TKey key]{get{if(TryGetValue(key,out var value))return value;throw new KeyNotFoundException();}set=>Set(key,value);}
    public void Add(TKey key,TValue value){if(Find(key)>=0)throw new ArgumentException("Duplicate key.");Set(key,value);}
    public bool ContainsKey(TKey key)=>Find(key)>=0;
    public bool TryGetValue(TKey key,out TValue value){var i=Find(key);if(i>=0){value=values[i];return true;}value=default!;return false;}
    private int Find(TKey key){var eq=CompatEqualityComparer<TKey>.Default;for(var i=0;i<used.Length;i++)if(used[i]&&eq.Equals(keys[i],key))return i;return -1;}
    private void Set(TKey key,TValue value){var i=Find(key);if(i>=0){values[i]=value;return;}if(Count==used.Length)Grow();for(i=0;i<used.Length;i++)if(!used[i]){used[i]=true;keys[i]=key;values[i]=value;Count++;return;}}
    private void Grow(){Array.Resize(ref keys,keys.Length*2);Array.Resize(ref values,values.Length*2);Array.Resize(ref used,used.Length*2);}
}

public static class CompatLinq
{
    public static IEnumerable<T> Where<T>(IEnumerable<T> source,Func<T,bool> predicate){foreach(var item in source)if(predicate(item))yield return item;}
    public static IEnumerable<TResult> Select<T,TResult>(IEnumerable<T> source,Func<T,TResult> selector){foreach(var item in source)yield return selector(item);}
    public static bool Any<T>(IEnumerable<T> source){using var e=source.GetEnumerator();return e.MoveNext();}
    public static int Count<T>(IEnumerable<T> source){var n=0;foreach(var _ in source)n++;return n;}
    public static T First<T>(IEnumerable<T> source){foreach(var item in source)return item;throw new InvalidOperationException();}
}
