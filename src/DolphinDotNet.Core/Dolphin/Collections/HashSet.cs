using System;
using System.Collections;
using System.Collections.Generic;
namespace Dolphin.Collections;
public class HashSet<T> : ISet<T>, IReadOnlyCollection<T>
{
    private readonly Dictionary<T,byte> items;
    public HashSet():this(0,null){}
    public HashSet(IEqualityComparer<T>? comparer):this(0,comparer){}
    public HashSet(int capacity):this(capacity,null){}
    public HashSet(int capacity,IEqualityComparer<T>? comparer){items=new Dictionary<T,byte>(capacity,comparer,true);}
    public HashSet(IEnumerable<T> source):this(source,null){}
    public HashSet(IEnumerable<T> source,IEqualityComparer<T>? comparer):this(0,comparer){if(source==null)throw new ArgumentNullException("source");foreach(var item in source)Add(item);}
    public int Count=>items.Count;
    public IEqualityComparer<T> Comparer=>items.Comparer;
    bool ICollection<T>.IsReadOnly=>false;
    public bool Add(T item)=>items.TryAdd(item,0);
    void ICollection<T>.Add(T item)=>Add(item);
    public bool Remove(T item)=>items.Remove(item);
    public bool Contains(T item)=>items.ContainsKey(item);
    public void Clear()=>items.Clear();
    public int EnsureCapacity(int capacity)=>items.EnsureCapacity(capacity);
    public void TrimExcess()=>items.TrimExcess();
    public void CopyTo(T[] array)=>CopyTo(array,0,Count);
    public void CopyTo(T[] array,int index)=>CopyTo(array,index,Count);
    public void CopyTo(T[] array,int index,int count){Dictionary<T,byte>.CheckCopy(array,index,count);if(count<0||count>Count)throw new ArgumentOutOfRangeException("count");foreach(var item in this){if(count--==0)break;array[index++]=item;}}
    public bool TryGetValue(T equalValue,out T actualValue)=>items.TryGetKey(equalValue,out actualValue);
    private HashSet<T> Set(IEnumerable<T> other){if(other==null)throw new ArgumentNullException("other");return new HashSet<T>(other,Comparer);}
    public void UnionWith(IEnumerable<T> other){if(other==null)throw new ArgumentNullException("other");foreach(var item in other)Add(item);}
    public void ExceptWith(IEnumerable<T> other){if(other==null)throw new ArgumentNullException("other");if(ReferenceEquals(this,other)){Clear();return;}foreach(var item in other)Remove(item);}
    public void IntersectWith(IEnumerable<T> other){var set=Set(other);var copy=new T[Count];CopyTo(copy);foreach(var item in copy)if(!set.Contains(item))Remove(item);}
    public void SymmetricExceptWith(IEnumerable<T> other){var set=Set(other);foreach(var item in set)if(!Remove(item))Add(item);}
    public bool IsSubsetOf(IEnumerable<T> other){var set=Set(other);foreach(var item in this)if(!set.Contains(item))return false;return true;}
    public bool IsProperSubsetOf(IEnumerable<T> other){var set=Set(other);return Count<set.Count&&IsSubsetOf(set);}
    public bool IsSupersetOf(IEnumerable<T> other){if(other==null)throw new ArgumentNullException("other");foreach(var item in other)if(!Contains(item))return false;return true;}
    public bool IsProperSupersetOf(IEnumerable<T> other){var set=Set(other);return Count>set.Count&&IsSupersetOf(set);}
    public bool Overlaps(IEnumerable<T> other){if(other==null)throw new ArgumentNullException("other");foreach(var item in other)if(Contains(item))return true;return false;}
    public bool SetEquals(IEnumerable<T> other){var set=Set(other);return Count==set.Count&&IsSubsetOf(set);}
    public int RemoveWhere(Predicate<T> match){if(match==null)throw new ArgumentNullException("match");var copy=new T[Count];CopyTo(copy);int count=0;foreach(var item in copy)if(match(item)&&Remove(item))count++;return count;}
    public Enumerator GetEnumerator()=>new Enumerator(items);
    IEnumerator<T> IEnumerable<T>.GetEnumerator()=>GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
    public struct Enumerator : IEnumerator<T>
    {
        private Dictionary<T,byte>.Enumerator inner;
        internal Enumerator(Dictionary<T,byte> items){inner=items.GetEnumerator();}
        public T Current=>inner.Current.Key;
        object IEnumerator.Current=>((IEnumerator)inner).Current is System.Collections.Generic.KeyValuePair<T,byte> pair?pair.Key:default!;
        public bool MoveNext()=>inner.MoveNext();
        public void Reset()=>inner.Reset();
        public void Dispose(){}
    }
}
