#nullable enable
using System;
using System.Collections.Generic;
internal static class Program
{
    private static int FinallyProbe()
    {
        var value = 1;
        try { value += 2; }
        finally { value += 4; }
        return value;
    }

    private static int CatchProbe()
    {
        try
        {
            int? value = null;
            return value.Value;
        }
        catch
        {
            return 11;
        }
    }

    private static int TypedCatchProbe()
    {
        try
        {
            int? value = null;
            return value.Value;
        }
        catch (ArgumentException)
        {
            return 12;
        }
        catch (InvalidOperationException)
        {
            return 13;
        }
    }

    private static int RethrowProbe()
    {
        try
        {
            try
            {
                int? value = null;
                return value.Value;
            }
            catch (InvalidOperationException)
            {
                throw;
            }
        }
        catch (Exception)
        {
            return 14;
        }
    }

    private static int ExplicitExceptionProbe()
    {
        try
        {
            throw new InvalidOperationException("explicit");
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message.Length == 8 ? 15 : -1;
        }
    }

    private static int EhIntegrationProbe()
    {
        var cleanup = 0;
        try
        {
            try
            {
                var values = new CompactList<int>();
                values.Add(3);
                if (!values.Contains(3)) return -1;
                int? missing = null;
                return missing.Value;
            }
            finally
            {
                cleanup += 4;
            }
        }
        catch (InvalidOperationException)
        {
            return cleanup == 4 ? 16 : -2;
        }
    }

    private static int StringPrimitiveProbe()
    {
        var value = "dolphin.net";
        if (value.Length != 11) return 1;
        if (value[0] != 'd' || value[8] != 'n') return 2;
        if (!value.Equals("dolphin.net") || value.Equals("Dolphin.net")) return 3;
        if (!value.StartsWith("dol") || value.StartsWith("net")) return 4;
        if (!value.EndsWith(".net") || value.EndsWith("dol")) return 5;
        if (!value.Contains("phin") || value.Contains("cube")) return 6;
        if (value.IndexOf("phin") != 3 || value.IndexOf("cube") != -1) return 7;
        if (!string.Equals(value.Substring(8), "net")) return 8;
        if (!string.Equals(value.Substring(0, 8), "dolphin.")) return 9;
        if (!string.Equals(string.Concat("game", "cube"), "gamecube")) return 10;
        if (!string.Equals(string.Concat(null, "cube"), "cube")) return 11;
        string? missing = null;
        if (!string.Equals(missing, null) || string.Equals(missing, value)) return 12;
        var duplicate = "dolphin.net";
        if (!object.ReferenceEquals(value, duplicate)) return 13;
        return 0;
    }

    private static int PrimitiveRepresentationProbe()
    {
        var bytes = new byte[2]; bytes[0] = 0xff; bytes[1] = 1;
        if (bytes[0] != 255 || bytes[1] != 1) return 1;

        var signed = new sbyte[1]; signed[0] = -2;
        if (signed[0] != -2) return 2;

        var chars = new char[2]; chars[0] = '\u03a9'; chars[1] = 'A';
        if (chars[0] != '\u03a9' || chars[1] != 'A') return 3;

        var shorts = new short[1]; shorts[0] = -1234;
        if (shorts[0] != -1234) return 4;

        var ushorts = new ushort[1]; ushorts[0] = 60000;
        if (ushorts[0] != 60000) return 5;

        var longs = new long[1]; longs[0] = 0x100000002L;
        if (longs[0] != 0x100000002L) return 6;

        var ulongs = new ulong[1]; ulongs[0] = 0xf000000000000002UL;
        if (ulongs[0] != 0xf000000000000002UL) return 7;

        return 0;
    }

    private static int SystemArrayProbe()
    {
        var values = new int[4];
        values[0] = 3; values[1] = 5; values[2] = 7; values[3] = 9;
        if (values.Rank != 1) return 1;
        if (values.LongLength != 4) return 1;
        if (values.GetLength(0) != 4) return 1;
        if (values.GetLowerBound(0) != 0) return 2;
        if (values.GetUpperBound(0) != 3) return 2;
        if (Array.IndexOf(values, 7) != 2) return 3;
        var copy = new int[4];
        Array.Copy(values, copy, values.Length);
        if (copy[0] != 3) return 4;
        if (copy[3] != 9) return 4;
        Array.Clear(copy, 1, 2);
        if (copy[0] != 3) return 5;
        if (copy[1] != 0) return 5;
        if (copy[2] != 0) return 5;
        if (copy[3] != 9) return 5;
        Array.Copy(values, 1, copy, 1, 2);
        if (copy[1] != 5) return 6;
        if (copy[2] != 7) return 6;
        return 0;
    }

    private static int ArrayInterfaceProbe()
    {
        var values = new int[3];
        values[0] = 2; values[1] = 4; values[2] = 6;
        var sum = 0;
        foreach (var value in values) sum += value;
        if (sum != 12) return 1;

        ICollection<int> collection = values;
        IList<int> list = values;
        IReadOnlyCollection<int> readOnlyCollection = values;
        IReadOnlyList<int> readOnlyList = values;
        if (collection.Count != 3 || !collection.IsReadOnly) return 2;
        if (!collection.Contains(4) || list.IndexOf(6) != 2) return 3;
        if (list[1] != 4 || readOnlyList[2] != 6 || readOnlyCollection.Count != 3) return 4;
        list[0] = 8;
        if (values[0] != 8) return 5;
        return 0;
    }

    private static int FormattingProbe()
    {
        if (!string.Equals(42.ToString(), "42")) return 1;
        if (!string.Equals((-17).ToString(), "-17")) return 2;
        if (!string.Equals(4294967295u.ToString(), "4294967295")) return 3;
        if (!string.Equals(9223372036854775807L.ToString(), "9223372036854775807")) return 4;
        if (!string.Equals(true.ToString(), "True")) return 5;
        if (!string.Equals('a'.ToString(), "a")) return 6;
        var text = string.Concat("Value: ", 42.ToString());
        if (!string.Equals(text, "Value: 42")) return 7;
        return 0;
    }

    private static void ThrowAcrossMethodBoundary()
    {
        throw new InvalidOperationException("cross-method");
    }

    private static int CrossMethodExceptionProbe()
    {
        var cleanup = 0;
        try
        {
            try { ThrowAcrossMethodBoundary(); }
            finally { cleanup += 7; }
        }
        catch (InvalidOperationException)
        {
            return cleanup == 7 ? 17 : -1;
        }
        return -2;
    }

    private static int NestedTypedCatchProbe()
    {
        try
        {
            try { ThrowAcrossMethodBoundary(); }
            catch (ArgumentException) { return -1; }
        }
        catch (InvalidOperationException) { return 18; }
        return -2;
    }

    private static int ThrowFromCatchProbe()
    {
        try
        {
            try { throw new ArgumentException("first"); }
            catch (ArgumentException) { throw new InvalidOperationException("second"); }
        }
        catch (InvalidOperationException) { return 20; }
        return -1;
    }

    private static int MultipleFinallyLeavesProbe(int value)
    {
        var cleanup=0;
        try
        {
            if(value==1)return 10;
            if(value==2)return 20;
            return 30;
        }
        finally { cleanup++; }
    }

    private static int Main()
    {
        var formatting = FormattingProbe(); if (formatting != 0) return 180 + formatting;
        var arrayInterfaces = ArrayInterfaceProbe(); if (arrayInterfaces != 0) return 160 + arrayInterfaces;
        var arrayStage = SystemArrayProbe(); if (arrayStage != 0) return 140 + arrayStage;
        var stringStage = StringPrimitiveProbe(); if (stringStage != 0) return 120 + stringStage;
        var primitiveStage = PrimitiveRepresentationProbe(); if (primitiveStage != 0) return 100 + primitiveStage;
        if (EhIntegrationProbe() != 16) return 95;
        if (CrossMethodExceptionProbe() != 17) return 96;
        if (NestedTypedCatchProbe() != 18) return 97;
        if (ThrowFromCatchProbe() != 20) return 99;
        if (MultipleFinallyLeavesProbe(1) != 10 || MultipleFinallyLeavesProbe(2) != 20 || MultipleFinallyLeavesProbe(3) != 30) return 100;
        if (ExplicitExceptionProbe() != 15) return 94;
        if (RethrowProbe() != 14) return 93;
        if (TypedCatchProbe() != 13) return 92;
        if (CatchProbe() != 11) return 91;
        if (FinallyProbe() != 7) return 90;
        _ = Shared<int>.Marker();
        _ = Shared<string>.Marker();
        _ = Identity(7);
        _ = Identity("reference");
        _ = Identity(9L);

        int? empty = null;
        int? present = 42;
        if (empty.HasValue) return 1;
        if (!present.HasValue || present.Value != 42 || present.GetValueOrDefault() != 42) return 2;
        if (empty.GetValueOrDefault() != 0 || empty.GetValueOrDefault(17) != 17) return 3;
        int? copy = present;
        if (!copy.HasValue || copy.Value != 42) return 7;

        long? wide = 0x100000002L;
        if (!wide.HasValue || wide.GetValueOrDefault() != 0x100000002L) return 4;
        object? boxedEmpty = empty;
        object? boxedPresent = present;
        if (boxedEmpty is not null || boxedPresent is not int || (int)boxedPresent != 42) return 5;
        object? boxedWide = wide;
        if (boxedWide is null) return 6;

        var stage2 = TestNullableEquality(present, empty); if (stage2 != 0) return stage2;
        stage2 = TestPairs(); if (stage2 != 0) return stage2;
        stage2 = TestSegment(); if (stage2 != 0) return stage2;
        stage2 = TestConcreteEnumeration(); if (stage2 != 0) return stage2;
        stage2 = TestGenericInterfaceEnumeration(); if (stage2 != 0) return stage2;
        stage2 = TestNonGenericInterfaceEnumeration(); if (stage2 != 0) return stage2;
        stage2 = TestCollectionContracts(); if (stage2 != 0) return stage2;
        return 0;
    }

    private static int TestNullableEquality(int? present, int? empty)
    {
        if (empty.GetHashCode() != 0) return 8;
        if (present.GetHashCode() != 42) return 16;
        if (!present.Equals(42)) return 12;
        if (present.Equals(43)) return 13;
        if (!empty.Equals(null)) return 14;
        if (empty.Equals(42)) return 15;
        return 0;
    }

    private static int TestPairs()
    {
        var pair = new KeyValuePair<string, string>("key", "value");
        if (pair.Key.Length != 3 || pair.Value.Length != 5) return 9;
        var widePair = new KeyValuePair<long, int>(0x100000002L, 7);
        if (widePair.Key != 0x100000002L || widePair.Value != 7) return 10;
        return 0;
    }

    private static int TestSegment()
    {
        var values = new[] { "zero", "one", "two" };
        var segment = new ArraySegment<string>(values, 1, 2);
        if (segment.Array is null || segment.Array.Length != 3 || segment.Offset != 1 || segment.Count != 2 || segment[0].Length != 3) return 11;
        return 0;
    }

    private static int TestConcreteEnumeration()
    {
        var values = new int[3]; values[0] = 3; values[1] = 4; values[2] = 5;
        var segment = new ArraySegment<int>(values, 1, 2);
        var sum = 0;
        foreach (var value in segment) sum += value;
        return sum == 9 ? 0 : 20;
    }

    private static int TestGenericInterfaceEnumeration()
    {
        var data = new int[3]; data[0] = 2; data[1] = 3; data[2] = 4;
        IEnumerable<int> values = new IntEnumerable(data);
        var sum = 0;
        foreach (var value in values) sum += value;
        return sum == 9 ? 0 : 21;
    }

    private static int TestNonGenericInterfaceEnumeration()
    {
        var data = new int[2]; data[0] = 6; data[1] = 7;
        System.Collections.IEnumerable values = new IntEnumerable(data);
        var sum = 0;
        foreach (int value in values) sum += value;
        return sum == 13 ? 0 : 22;
    }


    private static int TestCollectionContracts()
    {
        var list = new CompactList<int>();
        ICollection<int> collection = list;
        IList<int> indexed = list;
        IReadOnlyCollection<int> readOnlyCollection = list;
        IReadOnlyList<int> readOnlyList = list;
        collection.Add(4); collection.Add(6);
        if (collection.Count != 2 || readOnlyCollection.Count != 2) return 30;
        if (indexed[0] != 4 || readOnlyList[1] != 6) return 31;
        indexed[1] = 7;
        if (readOnlyList[1] != 7) return 32;
        if (collection.IsReadOnly) return 33;
        if (!collection.Contains(4) || collection.Contains(99) || indexed.IndexOf(7) != 1 || indexed.IndexOf(99) != -1) return 34;
        indexed.Insert(1, 5);
        if (indexed.Count != 3 || indexed[1] != 5 || indexed[2] != 7) return 35;
        indexed.RemoveAt(0);
        if (indexed.Count != 2 || indexed[0] != 5) return 36;
        if (!collection.Remove(5) || collection.Remove(99) || collection.Count != 1 || indexed[0] != 7) return 37;
        var copy = new int[2]; indexed.CopyTo(copy,0); if (copy[0] != 7) return 41;
        collection.Clear();
        if (collection.Count != 0) return 38;
        var refs = new CompactList<string>(); refs.Add("a"); refs.Insert(0,"b");
        if (!refs.Contains("a") || refs.IndexOf("b") != 0 || !refs.Remove("a") || refs.Count != 1) return 39;
        refs.Clear(); if (refs.Count != 0) return 40;
        return 0;
    }

    private static T Identity<T>(T value) => value;
}



internal sealed class CompactList<T> : IList<T>, IReadOnlyList<T>
{
    private T[] _items = new T[4];
    private int _count;
    public int Count => _count;
    public bool IsReadOnly => false;
    public T this[int index] { get => _items[index]; set => _items[index] = value; }
    public void Add(T item) { _items[_count++] = item; }
    public void Clear() { for (var i=0;i<_count;i++) _items[i]=default!; _count = 0; }
    public bool Contains(T item) => IndexOf(item) >= 0;
    public void CopyTo(T[] array, int arrayIndex) { for (var i=0;i<_count;i++) array[arrayIndex+i]=_items[i]; }
    public IEnumerator<T> GetEnumerator() => new CompactListEnumerator<T>(this);
    public int IndexOf(T item) { for(var i=0;i<_count;i++) if (object.Equals(_items[i],item)) return i; return -1; }
    public void Insert(int index,T item) { for (var i=_count;i>index;i--) _items[i]=_items[i-1]; _items[index]=item; _count++; }
    public bool Remove(T item) { var index=IndexOf(item); if(index<0)return false; RemoveAt(index); return true; }
    public void RemoveAt(int index) { _count--; for(var i=index;i<_count;i++) _items[i]=_items[i+1]; _items[_count]=default!; }
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

internal sealed class CompactListEnumerator<T> : IEnumerator<T>
{
    private readonly CompactList<T> _list; private int _index=-1;
    public CompactListEnumerator(CompactList<T> list)=>_list=list;
    public T Current=>_list[_index];
    object System.Collections.IEnumerator.Current=>Current!;
    public bool MoveNext(){_index++;return _index<_list.Count;}
    public void Reset()=>_index=-1;
    public void Dispose(){}
}

internal sealed class IntEnumerable : IEnumerable<int>
{
    private readonly int[] _values;
    public IntEnumerable(int[] values) => _values = values;
    public IEnumerator<int> GetEnumerator() => new IntEnumerator(_values);
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

internal sealed class IntEnumerator : IEnumerator<int>
{
    private readonly int[] _values;
    private int _index = -1;
    public IntEnumerator(int[] values) => _values = values;
    public int Current => _values[_index];
    object System.Collections.IEnumerator.Current => Current;
    public bool MoveNext() { _index++; return _index < _values.Length; }
    public void Reset() => _index = -1;
    public void Dispose() { }
}

internal static class Shared<T>
{
    public static int Marker() => 42;
}
