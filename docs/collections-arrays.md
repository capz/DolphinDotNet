# Collections and arrays (CL05, AR01, AR02)

Use the ordinary System APIs and reference DolphinDotNet.Core. These implementations are compiled into the application; they do not load the desktop framework at runtime. The supported subset includes:

- `Dictionary<TKey,TValue>`: capacity/comparer/enumerable/dictionary construction, indexer, Add/TryAdd, key/value searches, TryGetValue, Remove (including out value), Clear, EnsureCapacity/TrimExcess, copying and key/value/entry enumeration. Hash buckets use separate chains and reuse removed slots. Null dictionary keys are rejected. Insertions invalidate enumerators; removal, Clear and replacing an existing value follow modern .NET's non-invalidating behavior.
- `HashSet<T>`: capacity/comparer/enumerable construction, Add/Remove/Contains/TryGetValue, set operations and relationship queries, RemoveWhere, copying, capacity/trim and enumeration. Null reference elements are supported. Custom equality comparers are honored.
- `Queue<T>` and `Stack<T>`: capacity/enumerable construction, enqueue/push, dequeue/pop, peek and Try variants, Clear/Contains, CopyTo/ToArray, capacity/trim and versioned enumeration. Queue uses a circular buffer.
- `SortedList<TKey,TValue>` and `SortedDictionary<TKey,TValue>`: ordered dictionary operations, explicit/default comparers, key/value views and enumeration. Both currently use sorted arrays and binary searches. Insertion/removal is O(n), including SortedDictionary; it is not a balanced tree.
- `ReadOnlyCollection<T>`, `List<T>.AsReadOnly()` and `ReadOnlyDictionary<TKey,TValue>`: live views of the source collection. Changes through the source are visible; mutations through the view throw NotSupportedException.
- Generic `IDictionary`, `IReadOnlyDictionary`, `ISet` and the existing collection/enumeration contracts are mapped to the compiled implementations. KeyValuePair has a materialized layout, including reference tracing and aggregate boxing/unboxing.

Concurrent/immutable collections, serialization, non-generic dictionary interfaces, CollectionsMarshal and every newer convenience overload are outside this subset. Hash codes and iteration order are not persistence formats. Collections are not thread safe.

## Array operations

Generic `Array` overloads now support IndexOf/LastIndexOf, Reverse, Fill, Resize, BinarySearch, Sort, Find/FindLast/FindAll, FindIndex/FindLastIndex, Exists/TrueForAll, ForEach and ConvertAll. Searches use EqualityComparer<T>/Comparer<T> or the supplied comparer/predicate. Sort supports keys alone, Comparison<T>, ranges and paired keys/items, using an in-place heap sort. It is not stable. Null, range and capacity failures are checked.

Typed multidimensional arrays (`new int[rows,columns]`, reference/aggregate elements and higher ranks) support construction, indexed reads/writes, element addresses, row-major traversal, Length/LongLength/Rank and per-dimension bounds. Their references remain visible to GC. The runtime also understands explicit lower-bound metadata, but managed Array.CreateInstance and reflection-based element access are not exposed. Jagged typed arrays are supported separately.

Reference writes check the actual element type. Writable reference addresses require an exact element type to prevent covariance from bypassing checks. Type mismatch currently raises InvalidCastException, matching the existing native array contract, rather than .NET's ArrayTypeMismatchException. Full managed array covariance/casts, non-generic sorting, spans and reflection-based array APIs remain incomplete.

## Validation

`tests/WishlistSmoke` runs ordinary System API source under .NET 8 and AOT with forced GC. It covers collisions, growth/reuse, null keys/elements, custom equality, set algebra, queue wraparound, ordering, mutation during enumeration, live views, paired sorting, generic algorithms, multidimensional bounds, aggregate/reference tracing and covariance write rejection. CI runs sanitized 64-bit code, optimized 32-bit code and a PowerPC completion-word probe in Dolphin. `tests/managed_runtime_tests.c` covers the native runtime separately.
