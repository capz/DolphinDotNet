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

## Completed P0 compatibility milestones

### Phase 1 — structured exception handling

Complete for the compatibility-foundation scope: `throw`, `rethrow`, `leave`, typed catches, `finally`, nested unwinding and cross-method exception propagation are lowered through Value IR and the native runtime. Regression coverage includes typed catch selection, exceptions thrown from catch handlers, normal and exceptional `finally`, return-through-`finally`, nested `finally`, and rethrow object identity after a nested catch.

Exception filters and full CLR fault/filter parity remain outside this P0 compatibility gate and are tracked as later CLR-surface expansion.

### Phase 2 — Object / String / Array primitives

Complete for the P0 primitive set through ordinary framework calls recognized by the AOT compiler. Runtime/compiler coverage includes:

- `System.Object`: equality/reference equality, hash code, type identity and string conversion.
- `System.String`: length, indexer, equality, hash, two-string concat, substring, ordinal search, prefix/suffix and contains.
- `System.Array`: length/rank, dimension length, element access, copy, clear and compiler-generated one-dimensional enumeration.
- checked null/range behavior and propagation through the structured EH path.

These are runtime/compiler intrinsics, not replacement CoreLib declarations. Consequently the strict .NET Standard declaration scanner intentionally remains at 0/8,363 until compatible `System.*` contract declarations are introduced.

Validation gate: portable runtime/stress tests, managed compiler integration, control-flow/EH smoke, AOT smoke, generated C compilation and the devkitPPC GameCube ELF/DOL pipeline must all remain green.

### Phase 3 — equality / comparison infrastructure

Complete for the compatibility-foundation scope. DolphinDotNet.Core now owns its default equality and ordering infrastructure instead of delegating compatibility collections to the host framework's `EqualityComparer<T>` / `Comparer<T>`.

Implemented and characterized:

- `CompatEqualityComparer<T>.Default` with reference/null handling, `IEquatable<T>` preference, fallback object equality and stable hash delegation.
- `CompatComparer<T>.Default` with null ordering, `IComparable<T>` preference and non-generic `IComparable` fallback.
- `Foundation.Hash` / `Foundation.Compare`, `CompatList<T>` and `CompatDictionary<TKey,TValue>` use the DolphinDotNet comparer infrastructure.
- managed tests cover value types, reference/null semantics, hashing and ordering.
- AOT control-flow smoke covers the interface-dispatch/equality/ordering mechanics used by comparer contracts.

Constructed generic types from referenced assemblies are intentionally not yet lowered through Value IR because `TypeSpecification` instantiation belongs to the following generic-CoreLib phase. The strict .NET Standard declaration scanner therefore remains separate from this runtime/managed implementation milestone.

### Phase 4 — generic CoreLib foundation

Complete for the compatibility-foundation scope, with GameCube memory pressure treated as a primary design constraint.

Implemented:

- on-demand closed generic type metadata for `TypeSpecification` references; unused closed generic instantiations create no AOT metadata or code;
- closed generic field/method resolution and generic-parameter substitution in AOT signatures;
- closed generic interface identity and overload-safe interface dispatch;
- `CompatNullable<T>`, `CompatKeyValuePair<TKey,TValue>`, `CompatArraySegment<T>`;
- lean generic enumerable, enumerator, read-only collection/list and mutable collection/list contracts;
- compact `CompatList<T>` integration with allocation-free concrete struct enumeration;
- correct inline value-type field access for compact generic structs;
- primitive value-type fast paths remain distinct from generic metadata so boxing/unboxing and typed arrays retain their compact runtime representation.

Memory policy for this phase:

- generic instantiations are materialized only when reachable from the user's program;
- no blanket monomorphization of all possible generic types;
- empty `CompatList<T>` instances allocate no backing array; storage is created on first insertion;
- concrete list enumeration uses a struct enumerator and allocates no iterator object;
- interface-based enumeration remains available for compatibility and may box the struct enumerator, so allocation-sensitive game loops should prefer the concrete path;
- value structs remain inline and do not receive object headers until explicitly boxed;
- no additional runtime dictionaries or reflection tables were introduced for generic dispatch.

The strict .NET Standard declaration scanner remains separate from this runtime/managed compatibility milestone and therefore still reports the literal `System.*` contract surface independently.

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
