using System;
using System.Collections;
using System.Collections.Generic;
namespace Dolphin.Collections;
/// <summary>Read-only live view: later changes to the underlying list remain visible.</summary>
public class ReadOnlyCollection<T> : IList<T>, IReadOnlyList<T>
{
    private readonly IList<T> items;
    public ReadOnlyCollection(IList<T> list){items=list??throw new ArgumentNullException("list");}
    protected IList<T> Items=>items;
    public int Count=>items.Count;
    public T this[int index]=>items[index];
    bool ICollection<T>.IsReadOnly=>true;
    T IList<T>.this[int index] {get=>items[index];set=>throw new NotSupportedException();}
    public bool Contains(T value)=>items.Contains(value);
    public int IndexOf(T value)=>items.IndexOf(value);
    public void CopyTo(T[] array,int index)=>items.CopyTo(array,index);
    public IEnumerator<T> GetEnumerator()=>items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
    void ICollection<T>.Add(T value){throw new NotSupportedException();}
    void ICollection<T>.Clear(){throw new NotSupportedException();}
    bool ICollection<T>.Remove(T value){throw new NotSupportedException();}
    void IList<T>.Insert(int index,T value){throw new NotSupportedException();}
    void IList<T>.RemoveAt(int index){throw new NotSupportedException();}
}
