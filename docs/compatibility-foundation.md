# Compatibility foundation index

This index prioritizes APIs by **dependency unlock**, not raw API count. The first objective is to make ordinary compiler-generated C# and generic library code executable before pursuing broad surface-area compatibility.

## P0 — runtime gates

| Area | Minimum implementation | Why it unlocks compatibility |
| --- | --- | --- |
| Structured exception handling | `throw`, `rethrow`, `leave`, typed `catch`, `finally`, exception object propagation | EH metadata is emitted by common C# patterns and libraries even when exceptions are uncommon at runtime. |
| Interface dispatch | Complete interface slot lookup including inherited/generic interfaces and constrained value-type calls | Required by generic collections, equality/comparison and enumerable abstractions. |
| Value types/byrefs | Correct struct copy/layout, nested structs, `initobj`, `cpobj`, `ldobj`, `stobj`, boxing/unboxing and constrained calls | Prevents subtle corruption and unlocks generic value-type code. |
| Object primitives | `Equals`, `ReferenceEquals`, `GetHashCode`, `GetType`, `ToString` | Root dependency for equality, reflection-lite and collections. |
| String primitives | `Length`, indexer, equality/hash, concat, substring, search/prefix/suffix/contains, char conversion | Strings occur throughout diagnostics, parsing and application code. |
| Array primitives | length/rank, element access/copy/clear, generic enumeration | Foundation for collections and compiler-generated storage. |
| Delegates | construction, static/instance invocation, multicast combine/remove | Required by callbacks, events and LINQ. |

## P1 — small managed surface, large payoff

Implement these primarily in managed CoreLib once the P0 primitives exist:

- `Nullable<T>`
- `IEquatable<T>`, `IComparable`, `IComparable<T>`
- `EqualityComparer<T>` and `Comparer<T>`
- `IEnumerable`, `IEnumerator`, `IEnumerable<T>`, `IEnumerator<T>`
- `ICollection<T>`, `IList<T>`, `IReadOnlyCollection<T>`, `IReadOnlyList<T>`
- `List<T>`, then `Dictionary<TKey,TValue>`, `HashSet<T>`, `Stack<T>`, `Queue<T>`
- `KeyValuePair<TKey,TValue>`, `ArraySegment<T>`, `ValueTuple`
- primitive equality/comparison/conversion helpers
- `Math` / `MathF`
- `StringBuilder`

## P2 — cheap application-level compatibility

- `TimeSpan`
- `DateTime` over a tiny platform clock primitive
- `Random`
- basic `Console` mapped to the existing diagnostics path
- core LINQ operators: `Select`, `Where`, `Any`, `All`, `First`, `FirstOrDefault`, `Single`, `Count`, `Contains`, `Aggregate`
- reflection-lite: type identity/name, base type, interface checks and assignability

## Dependency chains to optimize

```
Object equality/hash
  -> IEquatable<T>
  -> EqualityComparer<T>.Default
  -> List<T>.Contains
  -> Dictionary<TKey,TValue> / HashSet<T>
  -> LINQ and third-party generic code
```

```
interface dispatch + delegates
  -> IEnumerable<T>
  -> collection abstractions
  -> LINQ
  -> callback/event-heavy application code
```

## Scanner policy

API compatibility reports should distinguish:

1. contract API exists,
2. implementation API exists,
3. runtime-characterized API actually executes correctly.

Future scanner work should add an **unlock score**: rank a missing primitive by the number of otherwise implementable/characterized APIs transitively blocked by it. This is more useful than optimizing the headline .NET Standard coverage percentage.

## Recommended implementation order

1. Complete interface/value-type correctness already in flight.
2. Structured EH.
3. Object/String/Array primitives.
4. Equality/comparison infrastructure and `Nullable<T>`.
5. Generic enumerable/collection interfaces.
6. `List<T>` and `Dictionary<TKey,TValue>`.
7. `StringBuilder`, primitive conversions, `Math`/`MathF`.
8. LINQ subset.
9. Re-run the compatibility scanner and choose the next tranche by measured dependency unlock.

Delegates are already implemented on main and should be treated as an existing foundation rather than reimplemented.
