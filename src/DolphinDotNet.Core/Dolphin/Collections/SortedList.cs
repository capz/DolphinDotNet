using System;
using System.Collections;
using System.Collections.Generic;
namespace Dolphin.Collections;
/// <summary>Ordered map using binary lookup and sorted, GC-traced parallel lists.</summary>
public class SortedList<TKey,TValue> : IDictionary<TKey,TValue>, IReadOnlyDictionary<TKey,TValue>
{
    private readonly List<TKey> keys;private readonly List<TValue> values;
    private readonly IComparer<TKey> comparer;private int version;
    public SortedList():this(0,null){}
    public SortedList(int capacity):this(capacity,null){}
    public SortedList(IComparer<TKey>? comparer):this(0,comparer){}
    public SortedList(int capacity,IComparer<TKey>? comparer){keys=new List<TKey>(capacity);values=new List<TValue>(capacity);this.comparer=comparer??Comparer<TKey>.Default;}
    public SortedList(IDictionary<TKey,TValue> source):this(source,null){}
    public SortedList(IDictionary<TKey,TValue> source,IComparer<TKey>? comparer):this(0,comparer){if(source==null)throw new ArgumentNullException("source");foreach(var pair in source)Add(pair.Key,pair.Value);}
    public int Count=>keys.Count;
    public int Capacity {get=>keys.Capacity;set{keys.Capacity=value;values.Capacity=value;}}
    public IComparer<TKey> Comparer=>comparer;
    public IList<TKey> Keys=>new ReadOnlyCollection<TKey>(keys);
    public IList<TValue> Values=>new ReadOnlyCollection<TValue>(values);
    bool ICollection<System.Collections.Generic.KeyValuePair<TKey,TValue>>.IsReadOnly=>false;
    ICollection<TKey> IDictionary<TKey,TValue>.Keys=>Keys;
    ICollection<TValue> IDictionary<TKey,TValue>.Values=>Values;
    IEnumerable<TKey> IReadOnlyDictionary<TKey,TValue>.Keys=>Keys;
    IEnumerable<TValue> IReadOnlyDictionary<TKey,TValue>.Values=>Values;
    private int Find(TKey key){if(key==null)throw new ArgumentNullException("key");return keys.BinarySearch(key,comparer);}
    public TValue this[TKey key] {get{int i=Find(key);if(i<0)throw new KeyNotFoundException();return values[i];}set{int i=Find(key);if(i<0){keys.Insert(~i,key);values.Insert(~i,value);}else values[i]=value;version++;}}
    public void Add(TKey key,TValue value){int i=Find(key);if(i>=0)throw new ArgumentException("An item with the same key already exists.");keys.Insert(~i,key);values.Insert(~i,value);version++;}
    public bool ContainsKey(TKey key)=>Find(key)>=0;
    public bool ContainsValue(TValue value)=>values.Contains(value);
    public int IndexOfKey(TKey key){int i=Find(key);return i<0?-1:i;}
    public int IndexOfValue(TValue value)=>values.IndexOf(value);
    public bool TryGetValue(TKey key,out TValue value){int i=Find(key);value=i<0?default!:values[i];return i>=0;}
    public bool Remove(TKey key){int i=Find(key);if(i<0)return false;RemoveAt(i);return true;}
    public void RemoveAt(int index){keys.RemoveAt(index);values.RemoveAt(index);version++;}
    public void Clear(){keys.Clear();values.Clear();version++;}
    public void TrimExcess(){keys.TrimExcess();values.TrimExcess();}
    public void CopyTo(System.Collections.Generic.KeyValuePair<TKey,TValue>[] array,int index){Dictionary<TKey,TValue>.CheckCopy(array,index,Count);for(int i=0;i<Count;i++)array[index++]=new System.Collections.Generic.KeyValuePair<TKey,TValue>(keys[i],values[i]);}
    void ICollection<System.Collections.Generic.KeyValuePair<TKey,TValue>>.Add(System.Collections.Generic.KeyValuePair<TKey,TValue> value)=>Add(value.Key,value.Value);
    bool ICollection<System.Collections.Generic.KeyValuePair<TKey,TValue>>.Contains(System.Collections.Generic.KeyValuePair<TKey,TValue> value){int i=Find(value.Key);return i>=0&&EqualityComparer<TValue>.Default.Equals(values[i],value.Value);}
    bool ICollection<System.Collections.Generic.KeyValuePair<TKey,TValue>>.Remove(System.Collections.Generic.KeyValuePair<TKey,TValue> value){int i=Find(value.Key);if(i<0||!EqualityComparer<TValue>.Default.Equals(values[i],value.Value))return false;RemoveAt(i);return true;}
    public IEnumerator<System.Collections.Generic.KeyValuePair<TKey,TValue>> GetEnumerator()=>new Enumerator(this);
    IEnumerator<System.Collections.Generic.KeyValuePair<TKey,TValue>> IEnumerable<System.Collections.Generic.KeyValuePair<TKey,TValue>>.GetEnumerator()=>GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
    public struct Enumerator : IEnumerator<System.Collections.Generic.KeyValuePair<TKey,TValue>>
    {
        private readonly SortedList<TKey,TValue> owner;private readonly int version;private int index;private System.Collections.Generic.KeyValuePair<TKey,TValue> current;
        internal Enumerator(SortedList<TKey,TValue> owner){this.owner=owner;version=owner.version;index=0;current=default;}
        public System.Collections.Generic.KeyValuePair<TKey,TValue> Current=>current;
        object IEnumerator.Current{get{if(index==0||index==owner.Count+1)throw new InvalidOperationException();return current;}}
        public bool MoveNext(){if(version!=owner.version)throw new InvalidOperationException("Collection was modified.");if(index<owner.Count){current=new System.Collections.Generic.KeyValuePair<TKey,TValue>(owner.keys[index],owner.values[index]);index++;return true;}index=owner.Count+1;current=default;return false;}
        public void Reset(){if(version!=owner.version)throw new InvalidOperationException();index=0;current=default;}
        public void Dispose(){}
    }
}
