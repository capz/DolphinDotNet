using System;
using System.Collections;
using System.Collections.Generic;
namespace Dolphin.Collections;

/// <summary>Growable AOT collection implementing the standard generic collection contracts.</summary>
public sealed class List<T> : IList<T>, IReadOnlyList<T>
{
    private T[] items = new T[4];
    private int count;
    private int version;
    public List(){}
    public List(IEnumerable<T> source){if(source==null)throw new ArgumentNullException("source");foreach(var item in source)Add(item);}
    public int Count => count;
    public bool IsReadOnly => false;
    public T this[int index]
    {
        get { Check(index); return items[index]; }
        set { Check(index); items[index] = value; version++; }
    }
    private void Check(int index) { if(index<0 || index>=count) throw new ArgumentOutOfRangeException("index"); }
    private void Grow()
    {
        if(count<items.Length) return;
        if(count>1073741823) throw new OutOfMemoryException();
        var next=new T[items.Length==0?4:items.Length*2];
        Array.Copy(items,0,next,0,count); items=next;
    }
    public void Add(T item) { Grow(); items[count++]=item; version++; }
    public void Clear() { Array.Clear(items,0,count); count=0; version++; }
    public int IndexOf(T item) { for(var i=0;i<count;i++) if(object.Equals(items[i],item))return i; return -1; }
    public bool Contains(T item) => IndexOf(item)>=0;
    public void Insert(int index,T item)
    {
        if(index<0 || index>count)throw new ArgumentOutOfRangeException("index");
        Grow(); Array.Copy(items,index,items,index+1,count-index); items[index]=item;count++;version++;
    }
    public void RemoveAt(int index)
    {
        Check(index); Array.Copy(items,index+1,items,index,count-index-1);
        count--; Array.Clear(items,count,1);version++;
    }
    public bool Remove(T item) { var index=IndexOf(item);if(index<0)return false;RemoveAt(index);return true; }
    public void CopyTo(T[] array,int arrayIndex)
    {
        if(array==null)throw new ArgumentNullException("array");
        if(arrayIndex<0)throw new ArgumentOutOfRangeException("arrayIndex");
        if(arrayIndex>array.Length || count>array.Length-arrayIndex)throw new ArgumentException("Destination is too small.");
        Array.Copy(items,0,array,arrayIndex,count);
    }
    public T[] ToArray() { var result=new T[count];CopyTo(result,0);return result; }
    public IEnumerator<T> GetEnumerator()=>new Enumerator(this);
    IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
    private sealed class Enumerator : IEnumerator<T>
    {
        private readonly List<T> owner;
        private readonly int version;
        private int index=-1;
        private bool disposed;
        public Enumerator(List<T> owner){this.owner=owner;version=owner.version;}
        private void Validate(){if(disposed)throw new ObjectDisposedException("Enumerator");if(version!=owner.version)throw new InvalidOperationException("Collection was modified.");}
        public T Current { get {Validate();if(index<0 || index>=owner.count)throw new InvalidOperationException("Enumerator is not positioned on an item.");return owner.items[index];} }
        object IEnumerator.Current=>Current!;
        public bool MoveNext(){Validate();if(index<owner.count)index++;return index<owner.count;}
        public void Reset(){Validate();index=-1;}
        public void Dispose(){disposed=true;}
    }
}
