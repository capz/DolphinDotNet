using System;
using System.Collections.Generic;
namespace Dolphin.Collections;

internal static class NativeComparison
{
    internal static bool Equal<T>(T x,T y)=>throw new NotSupportedException("AOT intrinsic");
    internal static int Hash<T>(T value)=>throw new NotSupportedException("AOT intrinsic");
    internal static int Compare<T>(T x,T y)=>throw new NotSupportedException("AOT intrinsic");
}
public class EqualityComparer<T> : IEqualityComparer<T>
{
    private static EqualityComparer<T>? instance;
    public static EqualityComparer<T> Default { get { if(instance==null)instance=new EqualityComparer<T>();return instance; } }
    public virtual bool Equals(T x,T y)=>NativeComparison.Equal(x,y);
    public virtual int GetHashCode(T value)=>NativeComparison.Hash(value);
}
public class Comparer<T> : IComparer<T>
{
    private static Comparer<T>? instance;
    public static Comparer<T> Default { get { if(instance==null)instance=new Comparer<T>();return instance; } }
    public virtual int Compare(T x,T y)=>NativeComparison.Compare(x,y);
    public static Comparer<T> Create(Comparison<T> comparison)=>new DelegateComparer(comparison);
    private sealed class DelegateComparer : Comparer<T>
    {
        private readonly Comparison<T> comparison;
        public DelegateComparer(Comparison<T> comparison){this.comparison=comparison??throw new ArgumentNullException("comparison");}
        public override int Compare(T x,T y)=>comparison(x,y);
    }
}
public static class Nullable
{
    public static bool Equals<T>(T? x,T? y) where T:struct => NativeComparison.Equal(x,y);
    public static int Compare<T>(T? x,T? y) where T:struct => NativeComparison.Compare(x,y);
}
