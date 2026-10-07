#nullable enable
using System;
using System.Collections.Generic;
internal static class Program
{
    private static int Main()
    {
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
        return 0;
    }

    private static int TestNullableEquality(int? present, int? empty)
    {
        if (empty.GetHashCode() != 0 || present.GetHashCode() != 42) return 8;
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

    private static T Identity<T>(T value) => value;
}

internal static class Shared<T>
{
    public static int Marker() => 42;
}
