using System;
using System.Collections;
using System.Collections.Generic;
namespace Dolphin.Collections;

/// <summary>Separate-chaining hash table with reusable slots and GC-traced key/value arrays.</summary>
public class Dictionary<TKey,TValue> : IDictionary<TKey,TValue>, IReadOnlyDictionary<TKey,TValue>
{
    private TKey[] keys;
    private TValue[] values;
    private int[] hashes,next,buckets;
    private int used,count,free=-1,version;
    private readonly IEqualityComparer<TKey> comparer;
    private readonly bool allowNull;
    public Dictionary():this(0,null){}
    public Dictionary(int capacity):this(capacity,null){}
    public Dictionary(IEqualityComparer<TKey>? comparer):this(0,comparer){}
    public Dictionary(int capacity,IEqualityComparer<TKey>? comparer):this(capacity,comparer,false){}
    internal Dictionary(int capacity,IEqualityComparer<TKey>? comparer,bool allowNull)
    {
        if(capacity<0)throw new ArgumentOutOfRangeException("capacity");
        this.comparer=comparer??EqualityComparer<TKey>.Default;this.allowNull=allowNull;
        keys=new TKey[capacity];values=new TValue[capacity];hashes=new int[capacity];next=new int[capacity];buckets=new int[capacity];
    }
    public Dictionary(IDictionary<TKey,TValue> source):this(source,null){}
    public Dictionary(IDictionary<TKey,TValue> source,IEqualityComparer<TKey>? comparer):this(0,comparer)
    {if(source==null)throw new ArgumentNullException("source");foreach(var pair in source)Add(pair.Key,pair.Value);}
    public Dictionary(IEnumerable<System.Collections.Generic.KeyValuePair<TKey,TValue>> source):this(source,null){}
    public Dictionary(IEnumerable<System.Collections.Generic.KeyValuePair<TKey,TValue>> source,IEqualityComparer<TKey>? comparer):this(0,comparer)
    {if(source==null)throw new ArgumentNullException("source");foreach(var pair in source)Add(pair.Key,pair.Value);}
    public int Count=>count;
    public IEqualityComparer<TKey> Comparer=>comparer;
    bool ICollection<System.Collections.Generic.KeyValuePair<TKey,TValue>>.IsReadOnly=>false;
    ICollection<TKey> IDictionary<TKey,TValue>.Keys=>Keys;
    ICollection<TValue> IDictionary<TKey,TValue>.Values=>Values;
    IEnumerable<TKey> IReadOnlyDictionary<TKey,TValue>.Keys=>Keys;
    IEnumerable<TValue> IReadOnlyDictionary<TKey,TValue>.Values=>Values;
    public TValue this[TKey key] {get{int i=Find(key);if(i<0)throw new KeyNotFoundException();return values[i];}set{Insert(key,value,1);}}
    public KeyCollection Keys=>new KeyCollection(this);
    public ValueCollection Values=>new ValueCollection(this);
    private int Hash(TKey key){if(key==null){if(!allowNull)throw new ArgumentNullException("key");return 0;}return comparer.GetHashCode(key)&int.MaxValue;}
    private int Find(TKey key){int hash=Hash(key);if(buckets.Length==0)return -1;for(int i=buckets[hash%buckets.Length]-1;i>=0;i=next[i])if(hashes[i]==hash&&comparer.Equals(keys[i],key))return i;return -1;}
    private bool Insert(TKey key,TValue value,int behavior)
    {
        int hash=Hash(key);
        if(buckets.Length!=0)for(int i=buckets[hash%buckets.Length]-1;i>=0;i=next[i])if(hashes[i]==hash&&comparer.Equals(keys[i],key)){
            if(behavior==0)throw new ArgumentException("An item with the same key already exists.");
            if(behavior==2)return false;values[i]=value;return true;
        }
        if(count==keys.Length)EnsureCapacity(count==0?4:count>int.MaxValue/2?int.MaxValue:count*2);
        int slot;if(free>=0){slot=free;free=next[slot];}else slot=used++;
        int bucket=hash%buckets.Length;hashes[slot]=hash;next[slot]=buckets[bucket]-1;keys[slot]=key;values[slot]=value;buckets[bucket]=slot+1;count++;version++;return true;
    }
    public void Add(TKey key,TValue value)=>Insert(key,value,0);
    public bool TryAdd(TKey key,TValue value)=>Insert(key,value,2);
    public bool ContainsKey(TKey key)=>Find(key)>=0;
    internal bool TryGetKey(TKey key,out TKey actual){int i=Find(key);actual=i<0?default!:keys[i];return i>=0;}
    public bool ContainsValue(TValue value){var eq=EqualityComparer<TValue>.Default;for(int i=0;i<used;i++)if(hashes[i]>=0&&eq.Equals(values[i],value))return true;return false;}
    public bool TryGetValue(TKey key,out TValue value){int i=Find(key);value=i<0?default!:values[i];return i>=0;}
    public bool Remove(TKey key)=>Remove(key,out _);
    public bool Remove(TKey key,out TValue value)
    {
        int hash=Hash(key);value=default!;if(buckets.Length==0)return false;int bucket=hash%buckets.Length,previous=-1;
        for(int i=buckets[bucket]-1;i>=0;previous=i,i=next[i])if(hashes[i]==hash&&comparer.Equals(keys[i],key)){
            if(previous<0)buckets[bucket]=next[i]+1;else next[previous]=next[i];
            value=values[i];keys[i]=default!;values[i]=default!;hashes[i]=-1;next[i]=free;free=i;count--;return true;
        }
        return false;
    }
    public void Clear(){if(used==0)return;Array.Clear(keys,0,used);Array.Clear(values,0,used);Array.Clear(hashes,0,used);Array.Clear(next,0,used);Array.Clear(buckets,0,buckets.Length);used=count=0;free=-1;}
    public int EnsureCapacity(int capacity)
    {
        if(capacity<0)throw new ArgumentOutOfRangeException("capacity");if(capacity<=keys.Length)return keys.Length;
        var k=new TKey[capacity];var v=new TValue[capacity];var h=new int[capacity];var n=new int[capacity];var b=new int[capacity];
        Array.Copy(keys,k,used);Array.Copy(values,v,used);Array.Copy(hashes,h,used);Array.Copy(next,n,used);
        for(int i=0;i<used;i++)if(h[i]>=0){int bucket=h[i]%capacity;n[i]=b[bucket]-1;b[bucket]=i+1;}
        keys=k;values=v;hashes=h;next=n;buckets=b;version++;return capacity;
    }
    public void TrimExcess()=>TrimExcess(count);
    public void TrimExcess(int capacity)
    {
        if(capacity<count)throw new ArgumentOutOfRangeException("capacity");if(capacity>=keys.Length)return;
        var k=new TKey[capacity];var v=new TValue[capacity];var h=new int[capacity];var n=new int[capacity];var b=new int[capacity];int at=0;
        for(int i=0;i<used;i++)if(hashes[i]>=0){int bucket=hashes[i]%capacity;k[at]=keys[i];v[at]=values[i];h[at]=hashes[i];n[at]=b[bucket]-1;b[bucket]=++at;}
        keys=k;values=v;hashes=h;next=n;buckets=b;used=count;free=-1;version++;
    }
    public void CopyTo(System.Collections.Generic.KeyValuePair<TKey,TValue>[] array,int index){CheckCopy(array,index,count);for(int i=0;i<used;i++)if(hashes[i]>=0)array[index++]=new System.Collections.Generic.KeyValuePair<TKey,TValue>(keys[i],values[i]);}
    internal static void CheckCopy<T>(T[] array,int index,int length){if(array==null)throw new ArgumentNullException("array");if(index<0)throw new ArgumentOutOfRangeException("index");if(index>array.Length-length)throw new ArgumentException("Destination is too small.");}
    void ICollection<System.Collections.Generic.KeyValuePair<TKey,TValue>>.Add(System.Collections.Generic.KeyValuePair<TKey,TValue> pair)=>Add(pair.Key,pair.Value);
    bool ICollection<System.Collections.Generic.KeyValuePair<TKey,TValue>>.Contains(System.Collections.Generic.KeyValuePair<TKey,TValue> pair){int i=Find(pair.Key);return i>=0&&EqualityComparer<TValue>.Default.Equals(values[i],pair.Value);}
    bool ICollection<System.Collections.Generic.KeyValuePair<TKey,TValue>>.Remove(System.Collections.Generic.KeyValuePair<TKey,TValue> pair){int i=Find(pair.Key);return i>=0&&EqualityComparer<TValue>.Default.Equals(values[i],pair.Value)&&Remove(pair.Key);}
    public Enumerator GetEnumerator()=>new Enumerator(this);
    IEnumerator<System.Collections.Generic.KeyValuePair<TKey,TValue>> IEnumerable<System.Collections.Generic.KeyValuePair<TKey,TValue>>.GetEnumerator()=>GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
    public struct Enumerator : IEnumerator<System.Collections.Generic.KeyValuePair<TKey,TValue>>
    {
        private readonly Dictionary<TKey,TValue> owner;private readonly int version;private int index;private System.Collections.Generic.KeyValuePair<TKey,TValue> current;
        internal Enumerator(Dictionary<TKey,TValue> owner){this.owner=owner;version=owner.version;index=0;current=default;}
        public System.Collections.Generic.KeyValuePair<TKey,TValue> Current=>current;
        object IEnumerator.Current {get{if(index==0||index==owner.used+1)throw new InvalidOperationException();return current;}}
        public bool MoveNext(){if(version!=owner.version)throw new InvalidOperationException("Collection was modified.");while(index<owner.used){int i=index++;if(owner.hashes[i]>=0){current=new System.Collections.Generic.KeyValuePair<TKey,TValue>(owner.keys[i],owner.values[i]);return true;}}index=owner.used+1;current=default;return false;}
        public void Reset(){if(version!=owner.version)throw new InvalidOperationException();index=0;current=default;}
        public void Dispose(){}
    }
    public sealed class KeyCollection : ICollection<TKey>, IReadOnlyCollection<TKey>
    {
        private readonly Dictionary<TKey,TValue> owner;
        public KeyCollection(Dictionary<TKey,TValue> owner){this.owner=owner??throw new ArgumentNullException("dictionary");}
        public int Count=>owner.Count;
        bool ICollection<TKey>.IsReadOnly=>true;
        public bool Contains(TKey value)=>owner.ContainsKey(value);
        public void CopyTo(TKey[] array,int index){CheckCopy(array,index,Count);for(int i=0;i<owner.used;i++)if(owner.hashes[i]>=0)array[index++]=owner.keys[i];}
        void ICollection<TKey>.Add(TKey value){throw new NotSupportedException();}
        void ICollection<TKey>.Clear(){throw new NotSupportedException();}
        bool ICollection<TKey>.Remove(TKey value){throw new NotSupportedException();}
        public Enumerator GetEnumerator()=>new Enumerator(owner);
        IEnumerator<TKey> IEnumerable<TKey>.GetEnumerator()=>GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
        public struct Enumerator : IEnumerator<TKey>
        {
            private Dictionary<TKey,TValue>.Enumerator inner;
            internal Enumerator(Dictionary<TKey,TValue> owner){inner=owner.GetEnumerator();}
            public TKey Current=>inner.Current.Key;
            object IEnumerator.Current=>((IEnumerator)inner).Current is System.Collections.Generic.KeyValuePair<TKey,TValue> pair?pair.Key:default!;
            public bool MoveNext()=>inner.MoveNext();
            public void Reset()=>inner.Reset();
            public void Dispose(){}
        }
    }
    public sealed class ValueCollection : ICollection<TValue>, IReadOnlyCollection<TValue>
    {
        private readonly Dictionary<TKey,TValue> owner;
        public ValueCollection(Dictionary<TKey,TValue> owner){this.owner=owner??throw new ArgumentNullException("dictionary");}
        public int Count=>owner.Count;
        bool ICollection<TValue>.IsReadOnly=>true;
        public bool Contains(TValue value)=>owner.ContainsValue(value);
        public void CopyTo(TValue[] array,int index){CheckCopy(array,index,Count);for(int i=0;i<owner.used;i++)if(owner.hashes[i]>=0)array[index++]=owner.values[i];}
        void ICollection<TValue>.Add(TValue value){throw new NotSupportedException();}
        void ICollection<TValue>.Clear(){throw new NotSupportedException();}
        bool ICollection<TValue>.Remove(TValue value){throw new NotSupportedException();}
        public Enumerator GetEnumerator()=>new Enumerator(owner);
        IEnumerator<TValue> IEnumerable<TValue>.GetEnumerator()=>GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
        public struct Enumerator : IEnumerator<TValue>
        {
            private Dictionary<TKey,TValue>.Enumerator inner;
            internal Enumerator(Dictionary<TKey,TValue> owner){inner=owner.GetEnumerator();}
            public TValue Current=>inner.Current.Value;
            object IEnumerator.Current=>((IEnumerator)inner).Current is System.Collections.Generic.KeyValuePair<TKey,TValue> pair?pair.Value:default!;
            public bool MoveNext()=>inner.MoveNext();
            public void Reset()=>inner.Reset();
            public void Dispose(){}
        }
    }
}
