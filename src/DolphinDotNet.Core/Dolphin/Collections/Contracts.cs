using System.Collections.Generic;
namespace Dolphin.Collections;
// These two-argument contracts retain real generic signatures; they cannot be
// reduced to the one-element synthetic array/list contracts in the compiler.
public interface IDictionary<TKey,TValue> : ICollection<System.Collections.Generic.KeyValuePair<TKey,TValue>>
{
    TValue this[TKey key] {get;set;}
    ICollection<TKey> Keys {get;}
    ICollection<TValue> Values {get;}
    void Add(TKey key,TValue value);
    bool ContainsKey(TKey key);
    bool Remove(TKey key);
    bool TryGetValue(TKey key,out TValue value);
}
public interface IReadOnlyDictionary<TKey,TValue> : IReadOnlyCollection<System.Collections.Generic.KeyValuePair<TKey,TValue>>
{
    TValue this[TKey key] {get;}
    IEnumerable<TKey> Keys {get;}
    IEnumerable<TValue> Values {get;}
    bool ContainsKey(TKey key);
    bool TryGetValue(TKey key,out TValue value);
}
public interface ISet<T> : ICollection<T>
{
    new bool Add(T item);
    void UnionWith(IEnumerable<T> other);
    void IntersectWith(IEnumerable<T> other);
    void ExceptWith(IEnumerable<T> other);
    void SymmetricExceptWith(IEnumerable<T> other);
    bool IsSubsetOf(IEnumerable<T> other);
    bool IsSupersetOf(IEnumerable<T> other);
    bool IsProperSubsetOf(IEnumerable<T> other);
    bool IsProperSupersetOf(IEnumerable<T> other);
    bool Overlaps(IEnumerable<T> other);
    bool SetEquals(IEnumerable<T> other);
}
