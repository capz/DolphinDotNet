# .NET Standard compatibility index

Audit date: 2026-10-09. Implementation baseline: Phase 7 plus compiled collections, text, cooperative async, FST and recoverable save replacement. This index describes ordinary compiled C# development; it is not a full .NET Standard conformance report.

The managed Core project targets `netstandard2.0`, while the compiler smoke applications target `net8.0`. A target framework supplies compile-time references; it does not make every referenced API available in the generated GameCube program. .NET Standard 2.0 is the initial API inventory baseline, not a claim that DolphinDotNet implements its whole contract.

## Status definitions

- **Compiled subset:** specific paths are lowered and exercised by compiled regression tests. Only the listed scope is established.
- **Compiler support:** lowering exists; broader semantic/runtime coverage remains incomplete or unverified.
- **Native only:** a C primitive exists, but normal managed API binding/implementation is absent.
- **Missing:** no end-to-end implementation was found in the audited production compiler/Core library.
- **Excluded:** permanently outside the project's stated scope.

## Developer-facing inventory

| ID | Area / APIs | State | Established scope and gaps |
| --- | --- | --- | --- |
| RT01 | Classes, constructors, inheritance, instance/static fields | Compiled subset | Lifecycle/static initialization/inherited field probes; not every layout or initialization edge case |
| RT02 | Virtual/interface calls, casts and type tests | Compiled subset | Managed lifecycle and collection dispatch; not all framework interfaces automatically materialize |
| RT03 | Generics | Compiled subset | Type-aware scalar/reference/aggregate specialization, embedded references and struct parameters/returns; framework-wide generic conformance remains unclaimed |
| RT04 | Exceptions, typed catches, rethrow, finally | Compiled subset | Nested regions, cross-method throws, rethrow after inner catch, finally continuations and GC during unwinding; filters/fault coverage is not established |
| RT05 | Managed allocation and GC | Compiled subset | Precise roots, embedded reference fields, pending exceptions, stress collection; native tests cover deep graphs/cycles/reuse/OOM |
| RT06 | Boxing/unboxing | Compiled subset | Scalar widths, floats, nullable primitives, aggregate boxing and boxed equality/hash; arbitrary aggregate unboxing remains outside the tested scope |
| RT07 | Delegates / Action / Func foundation | Compiler support | Construction/invocation and target roots exist; multicast combine/remove and complete signature coverage remain gaps |
| EQ01 | `Object.Equals` | Compiled subset | Identity/null, boxed scalar/string/value equality and object overrides; generic typed equality is selected separately |
| EQ02 | `Object.GetHashCode` | Compiled subset | Object overrides, identity fallback, boxed values and constrained generic hash paths; hash values are implementation-defined |
| EQ03 | `IEquatable<T>`, `IComparable<T>`, `IComparable` | Compiled subset | MethodImpl-aware explicit contracts and typed default comparison; unsupported ordering throws rather than comparing addresses |
| EQ04 | `EqualityComparer<T>`, `Comparer<T>`, `StringComparer` | Compiled subset | Defaults/custom/delegate comparers, scalar/nullable/value/reference cases; ordinal and Unicode ordinal-ignore-case strings. Culture comparers remain unavailable |
| NU01 | `Nullable<T>` construction, `HasValue`, `Value`, `GetValueOrDefault` | Compiled subset | int/long tested, private parameter copies and byref replacement; arbitrary struct payloads not established |
| NU02 | Nullable `Equals(object)`, `GetHashCode`, boxing | Compiled subset | Empty/present int/long equality/hash and boxing/unboxing paths; not full generic equality infrastructure |
| NU03 | Static `Nullable.Equals<T>`, `Nullable.Compare<T>`, reflection helpers | Compiled subset | Static equality/comparison use the default generic comparers; int/long regressions covered. Reflection helpers remain unsupported |
| AR01 | One-dimensional scalar/reference arrays | Compiled subset | Widths, allocation/bounds/null/overflow checks, references and native covariance tests; multidimensional arrays not established |
| AR02 | `Array.Length`, `LongLength`, `Rank`, bounds queries, `Clear`, `Copy`, `IndexOf` | Compiled subset | Selected overloads and array interface paths; general comparer-driven searches and sorting not established |
| ST01 | `String.Length`, indexer, equality, `Substring`, two-string `Concat` | Compiled subset | UTF-16 storage/characters, selected operations and GC protection; full overload/exception matrix not established |
| ST02 | String `StartsWith`, `EndsWith`, `Contains`, `IndexOf` | Compiler support | One-argument intrinsic paths; ordinal native behavior does not establish .NET culture-sensitive defaults/overloads |
| ST03 | Primitive parameterless `ToString` | Compiled subset | Int32/Int64/UInt32/UInt64/Boolean/Char paths; format strings, parsing and culture missing |
| ST04 | `StringBuilder`, `String.Format`, composite/interpolated formatting | Missing | Compiler-generated interpolation can require unsupported framework APIs |
| CL01 | `IEnumerable<T>`, `IEnumerator<T>`, nongeneric counterparts, `IDisposable` | Compiled subset | Custom class enumeration, explicit nongeneric Current and cleanup paths; standard array-as-interface enumeration and all variance cases not established |
| CL02 | `ICollection<T>`, `IList<T>`, `IReadOnlyCollection<T>`, `IReadOnlyList<T>` | Compiled subset | Synthetic contracts, selected array calls and custom collection dispatch; contract semantics need fuller regression coverage |
| CL03 | `KeyValuePair<TKey,TValue>`, `ArraySegment<T>` | Compiled subset | Construction/accessors, selected enumerator paths, embedded reference-field tracing; full interface surface not established |
| CL04 | Standard growable list and array enumerable | Compiled subset | Standard List<T> maps to the managed implementation: arrays/enumerables, ranges, searches/predicates, sort/binary search and value-copy enumerators. Nongeneric IList remains unavailable |
| CL05 | `Dictionary`, `HashSet`, `Queue`, `Stack`, sorted/read-only collections | Missing | Equality/hash/order infrastructure is a prerequisite; no standard implementations found |
| CL06 | Iterator blocks (`yield return`) | Unverified | Generated state-machine semantics not established by manually authored enumerator tests |
| LQ01 | `System.Linq.Enumerable` | Missing | Select/Where/Any/All/Count/ToArray/etc. need managed implementation plus enumerable/delegate support |
| IO01 | Binary `System.IO.File` | Compiled subset | Normal compiled File calls map to managed facade/native backend; selected Open/Copy/Move/Delete and byte-array operations. See [exact scope](storage-runtime.md) |
| IO02 | Directory and streams/text IO | Compiled subset | Stream/FileStream/MemoryStream, readers/writers, text files, pattern/recursive enumeration and cooperative async overloads; exact overloads and limitations are in [storage scope](storage-runtime.md) |
| IO03 | Path helpers | Compiled subset | Selected normal Path calls use console device-prefix grammar. Guid/AppContext and full desktop path semantics remain missing |
| IO04 | Console | Partial integration | GameCube WriteLine intrinsic and native overlay exist; general `System.Console` overload family is not registered in production intrinsic table |
| NM01 | `Math`, `Random` | Native only | Integer Abs/Min/Max and PRNG primitives; normal managed APIs not wired up |
| NM02 | Floating point, Decimal, BigInteger, Convert/Parse families | Missing/unverified broad support | IR kind names and signature recognition do not establish numeric semantics or library coverage |
| TX01 | `Encoding`, globalization, culture and time/date APIs | Compiled subset; other families missing | UTF-8/UTF-16/ASCII/Latin-1 codecs, strict fallback exceptions and BOM/text buffering; globalization and time/date APIs remain absent |
| TH01 | Tasks, async/await, cancellation, Thread and synchronization | Compiled cooperative subset | Task/Task<T>, builders/awaiters, completion sources, Yield/Run, WhenAll/WhenAny, ConfigureAwait, cancellation and chunked IO. No thread pool, Thread/locks, timers or synchronization contexts |
| NW01 | `System.Net`, sockets, HttpClient | Native only / missing facade | Native nonblocking UDP foundation; ordinary .NET socket/HTTP API bindings absent |
| SR01 | JSON/XML, Regex, serialization, compression | Missing | Framework/NuGet assemblies cannot be assumed to run through the AOT pipeline |
| RF01 | Full reflection and dynamic assembly loading | Excluded | Static closed-world metadata and supported type checks remain allowed |

## What is practical today

Small, explicitly tested C# programs using the supported object/control-flow/runtime subset can compile through AOT. Custom collection/algorithm code can work when all reachable calls and emitted CIL are supported. Ordinary .NET library use still requires inspecting its complete reachable dependency graph; a successful Roslyn build is insufficient.

Standard List, selected streams/text and cooperative async now have compiled smoke coverage. LINQ, Dictionary and common formatting-heavy NuGet libraries are not a drop-in development surface. Phase 7 acceptance validates a constrained execution model, not the whole Base Class Library.

## Evidence

- [Green Phase 7 CI run 996](https://github.com/capz/DolphinDotNet/actions/runs/37862418540): managed/compiler, portable runtime and GameCube build jobs.
- `tests/GenericSharingSmoke/Program.cs`: combined compiled runtime/generic/nullable/collection regressions; its helper classes are test fixtures.
- `tests/ControlFlowSmoke/Program.cs`, `tools/DolphinDotNet.Compiler.Tests/Program.cs`: control flow and compiler characterization.
- `tests/managed_runtime_tests.c`, `tests/runtime_tests.c`: native mechanisms; native-only evidence must not promote a normal managed API to supported.
- `tools/DolphinDotNet.Compiler/IntrinsicRegistry.cs`, `ValueIrImporter.cs`, `ValueCBackend.cs`: production managed call bindings and emitted behavior.
- `AotCompiler.EnsureEnumerationContracts`: synthetic generic/nongeneric collection contracts.
- GameCube ELF/DOL linking is verified. Hardware/Dolphin execution and runtime stack high-water are still separate pending verification.

## Coverage measurement limits

No defensible full-contract coverage percentage has been measured. The existing `DolphinDotNet.ApiCompat` tool compares metadata surfaces, not compiled semantics. It compares raw signature blobs, whose type-token encodings can differ between assemblies; nested type ownership, accessibility, events and inherited API surface also need normalization before using its output as an authoritative .NET Standard inventory.

The Core assembly currently contains a small `DolphinDotNet.Runtime` facade, not a replacement implementation of the `netstandard.dll` API surface. Comparing it directly with the full reference assembly would not measure intrinsic/compiler support.

A reproducible contract report needs: pinned reference assembly/version; normalized namespace/type/member signatures; explicit intrinsic/managed/native/excluded classifications; exact overload-to-test mappings; and separate compile, host-execution and target-execution evidence. Until then this audited index is qualitative and deliberately scoped.

## Implementation order requested

1. Equality/comparison: object identity/value semantics, consistent hashing, typed equality/comparison interfaces, default/custom comparers and null ordering.
2. Nullable: connect equality/comparison infrastructure, static Equals/Compare, payload types and boxed/null behavior.
3. Generic enumerable/collection contracts: inheritance/dispatch, array adapters, enumerator validity/disposal and standard collection implementations using the shared comparer infrastructure.
4. Binary file/directory/stream foundation following [the System.IO design](system-io-design.md); lazy enumeration depends on stage 3, text helpers also depend on Encoding.
5. LINQ, text/formatting and remaining common library families, each promoted by exact compiled tests.

## Reference

[Microsoft's .NET Standard overview](https://learn.microsoft.com/en-us/dotnet/standard/net-standard) defines the API contract. Project-specific unsupported areas and permanent exclusions remain explicit rather than being counted as implementation.

Library evidence: `tests/LibrarySmoke`, including explicit deferred state-machine and GC-stress probes. Storage evidence: `tests/StorageSmoke`, `tests/storage_host.c`, `source/storage.c`, `source/storage_gamecube.c`, and the collections/storage CI step. Portable injected filesystem/transaction failures are tested. Hardware execution remains separate acceptance work.
