using System;
using System.Collections;
using System.Collections.Generic;
namespace Dolphin.Collections;
/// <summary>Array adapter for generic and nongeneric enumerable contracts.</summary>
public sealed class ArrayEnumerable<T> : IEnumerable<T>
{
    private readonly T[] items;
    public ArrayEnumerable(T[] items){if(items==null)throw new ArgumentNullException("items");this.items=items;}
    public IEnumerator<T> GetEnumerator()=>new Enumerator(items);
    public static IEnumerator<T> Enumerate(T[] items)=>new Enumerator(items);
    IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
    private sealed class Enumerator : IEnumerator<T>
    {
        private readonly T[] items;private int index=-1;
        public Enumerator(T[] items){this.items=items;}
        public T Current {get{if(index<0 || index>=items.Length)throw new InvalidOperationException("Enumerator is not positioned on an item.");return items[index];}}
        object IEnumerator.Current=>Current!;
        public bool MoveNext(){if(index<items.Length)index++;return index<items.Length;}
        public void Reset(){index=-1;}
        public void Dispose(){}
    }
}
