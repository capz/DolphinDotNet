using System;
using System.Collections;
using System.Collections.Generic;
namespace Dolphin.Collections;
/// <summary>Live read-only wrapper; the dictionary and comparer remain caller owned.</summary>
public class ReadOnlyDictionary<TKey,TValue> : IDictionary<TKey,TValue>, IReadOnlyDictionary<TKey,TValue>
{
    private readonly IDictionary<TKey,TValue> items;
    public ReadOnlyDictionary(IDictionary<TKey,TValue> dictionary){items=dictionary??throw new ArgumentNullException("dictionary");}
    protected IDictionary<TKey,TValue> Dictionary=>items;
    public int Count=>items.Count;
    public TValue this[TKey key]=>items[key];
    TValue IDictionary<TKey,TValue>.this[TKey key] {get=>items[key];set=>throw new NotSupportedException();}
    bool ICollection<System.Collections.Generic.KeyValuePair<TKey,TValue>>.IsReadOnly=>true;
    public KeyCollection Keys=>new KeyCollection(items.Keys);
    ICollection<TKey> IDictionary<TKey,TValue>.Keys=>Keys;
    public ValueCollection Values=>new ValueCollection(items.Values);
    ICollection<TValue> IDictionary<TKey,TValue>.Values=>Values;
    IEnumerable<TKey> IReadOnlyDictionary<TKey,TValue>.Keys=>Keys;
    IEnumerable<TValue> IReadOnlyDictionary<TKey,TValue>.Values=>Values;
    public bool ContainsKey(TKey key)=>items.ContainsKey(key);
    public bool TryGetValue(TKey key,out TValue value)=>items.TryGetValue(key,out value);
    public IEnumerator<System.Collections.Generic.KeyValuePair<TKey,TValue>> GetEnumerator()=>items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
    public void CopyTo(System.Collections.Generic.KeyValuePair<TKey,TValue>[] array,int index)=>items.CopyTo(array,index);
    void IDictionary<TKey,TValue>.Add(TKey key,TValue value){throw new NotSupportedException();}
    bool IDictionary<TKey,TValue>.Remove(TKey key){throw new NotSupportedException();}
    void ICollection<System.Collections.Generic.KeyValuePair<TKey,TValue>>.Add(System.Collections.Generic.KeyValuePair<TKey,TValue> pair){throw new NotSupportedException();}
    void ICollection<System.Collections.Generic.KeyValuePair<TKey,TValue>>.Clear(){throw new NotSupportedException();}
    bool ICollection<System.Collections.Generic.KeyValuePair<TKey,TValue>>.Contains(System.Collections.Generic.KeyValuePair<TKey,TValue> pair)=>items.Contains(pair);
    bool ICollection<System.Collections.Generic.KeyValuePair<TKey,TValue>>.Remove(System.Collections.Generic.KeyValuePair<TKey,TValue> pair){throw new NotSupportedException();}
    public sealed class KeyCollection : ICollection<TKey>, IReadOnlyCollection<TKey>
    {
        private readonly ICollection<TKey> source;
        internal KeyCollection(ICollection<TKey> source){this.source=source;}
        public int Count=>source.Count;
        bool ICollection<TKey>.IsReadOnly=>true;
        public bool Contains(TKey item)=>source.Contains(item);
        public void CopyTo(TKey[] array,int index)=>source.CopyTo(array,index);
        public IEnumerator<TKey> GetEnumerator()=>source.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
        void ICollection<TKey>.Add(TKey item){throw new NotSupportedException();}
        void ICollection<TKey>.Clear(){throw new NotSupportedException();}
        bool ICollection<TKey>.Remove(TKey item){throw new NotSupportedException();}
    }
    public sealed class ValueCollection : ICollection<TValue>, IReadOnlyCollection<TValue>
    {
        private readonly ICollection<TValue> source;
        internal ValueCollection(ICollection<TValue> source){this.source=source;}
        public int Count=>source.Count;
        bool ICollection<TValue>.IsReadOnly=>true;
        public bool Contains(TValue item)=>source.Contains(item);
        public void CopyTo(TValue[] array,int index)=>source.CopyTo(array,index);
        public IEnumerator<TValue> GetEnumerator()=>source.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
        void ICollection<TValue>.Add(TValue item){throw new NotSupportedException();}
        void ICollection<TValue>.Clear(){throw new NotSupportedException();}
        bool ICollection<TValue>.Remove(TValue item){throw new NotSupportedException();}
    }
}
