using System;

namespace DolphinDotNet.Compatibility;

public abstract class CompatEqualityComparer<T>
{
    private static readonly CompatEqualityComparer<T> instance=new DefaultCompatEqualityComparer<T>();
    public static CompatEqualityComparer<T> Default=>instance;
    public abstract bool Equals(T? x,T? y);
    public abstract int GetHashCode(T value);

    private sealed class DefaultCompatEqualityComparer<TValue>:CompatEqualityComparer<TValue>
    {
        public override bool Equals(TValue? x,TValue? y)
        {
            if(object.ReferenceEquals(x,y))return true;
            if(x is null||y is null)return false;
            if(x is IEquatable<TValue> equatable)return equatable.Equals(y);
            return object.Equals(x,y);
        }

        public override int GetHashCode(TValue value)=>value is null?0:value.GetHashCode();
    }
}

public abstract class CompatComparer<T>
{
    private static readonly CompatComparer<T> instance=new DefaultCompatComparer<T>();
    public static CompatComparer<T> Default=>instance;
    public abstract int Compare(T? x,T? y);

    private sealed class DefaultCompatComparer<TValue>:CompatComparer<TValue>
    {
        public override int Compare(TValue? x,TValue? y)
        {
            if(object.ReferenceEquals(x,y))return 0;
            if(x is null)return -1;
            if(y is null)return 1;
            if(x is IComparable<TValue> generic)return generic.CompareTo(y);
            if(x is IComparable comparable)return comparable.CompareTo(y);
            throw new ArgumentException("Type does not provide a comparison contract.");
        }
    }
}
