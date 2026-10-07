using System;

namespace DolphinDotNet.Compatibility;

public sealed class CompatEqualityComparer<T>
{
    private static readonly CompatEqualityComparer<T> instance=new();
    private CompatEqualityComparer(){}
    public static CompatEqualityComparer<T> Default=>instance;

    public bool Equals(T? x,T? y)
    {
        if(object.ReferenceEquals(x,y))return true;
        if(x is null||y is null)return false;
        if(x is ICompatEquatable<T> equatable)return equatable.Equals(y);
        return object.Equals(x,y);
    }

    public int GetHashCode(T value)
    {
        if(value is null)return 0;
        if(value is ICompatEquatable<T> equatable)return equatable.GetCompatHashCode();
        return ((object)value).GetHashCode();
    }
}

public sealed class CompatComparer<T>
{
    private static readonly CompatComparer<T> instance=new();
    private CompatComparer(){}
    public static CompatComparer<T> Default=>instance;

    public int Compare(T? x,T? y)
    {
        if(object.ReferenceEquals(x,y))return 0;
        if(x is null)return -1;
        if(y is null)return 1;
        if(x is ICompatComparable<T> generic)return generic.CompareTo(y);
        throw new Exception("Type does not provide a comparison contract.");
    }
}
