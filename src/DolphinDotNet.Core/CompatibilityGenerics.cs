using System;

namespace DolphinDotNet.Compatibility;

public readonly struct CompatNullable<T> where T:struct
{
    private readonly T value;
    public CompatNullable(T value){this.value=value;HasValue=true;}
    public bool HasValue{get;}
    public T Value=>HasValue?value:throw new Exception("Nullable object must have a value.");
    public T GetValueOrDefault()=>value;
    public T GetValueOrDefault(T defaultValue)=>HasValue?value:defaultValue;
    public override bool Equals(object? other)=>HasValue&&other is T item&&CompatEqualityComparer<T>.Default.Equals(value,item);
    public override int GetHashCode()=>HasValue?CompatEqualityComparer<T>.Default.GetHashCode(value):0;
    public override string ToString()=>HasValue?((object)value).ToString():"";
    public static implicit operator CompatNullable<T>(T value)=>new(value);
}

public readonly struct CompatKeyValuePair<TKey,TValue>
{
    public CompatKeyValuePair(TKey key,TValue value){Key=key;Value=value;}
    public TKey Key{get;}
    public TValue Value{get;}
}

public readonly struct CompatArraySegment<T>
{
    private readonly T[] array;
    public CompatArraySegment(T[] array):this(array,0,array?.Length??throw new Exception("Array is null.")){}
    public CompatArraySegment(T[] array,int offset,int count)
    {
        if(array is null)throw new Exception("Array is null.");
        if((uint)offset>(uint)array.Length||(uint)count>(uint)(array.Length-offset))throw new Exception("Array segment range is invalid.");
        this.array=array;Offset=offset;Count=count;
    }
    public T[] Array=>array;
    public int Offset{get;}
    public int Count{get;}
    public T this[int index]=>index>=0&&index<Count?array[Offset+index]:throw new Exception("Array segment index is invalid.");
}

public interface ICompatEquatable<T>
{
    bool Equals(T other);
    int GetCompatHashCode();
}

public interface ICompatComparable<T>
{
    int CompareTo(T other);
}

public interface ICompatEnumerator<out T>
{
    T Current{get;}
    bool MoveNext();
}

public interface ICompatEnumerable<out T>
{
    ICompatEnumerator<T> GetCompatEnumerator();
}

public interface ICompatReadOnlyCollection<out T>:ICompatEnumerable<T>
{
    int Count{get;}
}

public interface ICompatReadOnlyList<out T>:ICompatReadOnlyCollection<T>
{
    T this[int index]{get;}
}

public interface ICompatCollection<T>:ICompatEnumerable<T>
{
    int Count{get;}
    void Add(T item);
    void Clear();
    bool Contains(T item);
    bool Remove(T item);
}

public interface ICompatList<T>:ICompatCollection<T>,ICompatReadOnlyList<T>
{
    new T this[int index]{get;set;}
    int IndexOf(T item);
    void Insert(int index,T item);
    void RemoveAt(int index);
}
