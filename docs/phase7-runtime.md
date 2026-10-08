# Phase 7 — Runtime completeness

Status: **acceptance gates satisfied — target execution remains separate**. Runtime completeness means the constrained C# execution model needed for GameCube games, **not** complete desktop .NET compatibility.

## Permanent exclusions

- Full reflection
- Dynamic assembly loading

Static compile-time metadata and narrowly defined type operations remain supported.

## Audit of the current native runtime

Already implemented in `source/managed_runtime.c` and exercised by `tests/managed_runtime_tests.c`:

- Managed object allocation, type metadata, fields and interface/virtual dispatch primitives
- UTF-16 strings, scalar and reference arrays, bounds/type checks
- Explicit GC roots, frame roots, non-moving mark/sweep, free-block reuse, stress mode
- Managed exception objects, exception kinds and setjmp-based exception frames
- Delegate, boxed integer and core collection primitives

**Important:** Having native primitives does not imply that ordinary C# compiler output can exercise every feature.

## Acceptance gates

1. **Object lifecycle:** validate generated constructors, instance/static fields and inheritance through compiled C#.
2. **GC:** ensure every compiler-generated live reference has a precise root; protect temporary allocation inputs; verify pending exceptions, deep graphs, cycles, fragmentation, OOM and constrained-heap behavior.
3. **Exceptions:** verify nested try/catch/finally, rethrow, cleanup and GC-root restoration through compiled C#.
4. **Arrays/strings:** validate overflow, null, bounds, covariance, UTF-16 semantics and allocation failures.
5. **Type system:** test virtual/interface dispatch, casting, boxing and value-type field tracing.
6. **Generics:** validate reference sharing, value specialization and generic dispatch.
7. **Integration:** run a compiled managed smoke application combining the above on the portable host.
8. **GameCube:** produce a DOL with the runtime and measure heap/stack footprint; hardware or emulator execution remains a separate verification gate.

## Automated acceptance evidence

The acceptance tests exercise generated C, rather than checking only the availability of native primitives.

| Gate | Regression evidence |
| --- | --- |
| Object lifecycle | `LifecycleProbe`: constructors, static initialization, inherited fields, virtual and interface calls |
| GC | Managed smoke runs with collection before every allocation in a 64 KiB heap; native tests cover pending exceptions, deep graphs, cycles, reuse, fragmentation and OOM |
| Exceptions | Nested typed catches, cross-method throws, rethrow after an inner catch, multiple finally continuations, exceptions inside finally, and GC during unwinding |
| Arrays/strings | Compiled primitive widths, array/interface contracts and UTF-16 operations; native overflow, null, bounds, covariance and allocation-failure tests |
| Type system | Compiled virtual/interface calls, casts, nullable/scalar boxing, and reference tracing inside an object’s `KeyValuePair` field |
| Generics | Shared reference representations; separate scalar/wide-value bodies; closed generic fields; reference collection enumeration; 64-bit interface results marshalled through an explicit result slot |
| Portable integration | Normal, GC-stress and optimized ASan/UBSan execution; additional 32-bit host execution to validate pointer-width assumptions |
| GameCube build/footprint | ELF/DOL build; ELF sections, symbol sizes, and compiler stack-usage files included in the CI artifact |

CI run [995](https://github.com/capz/DolphinDotNet/actions/runs/37862251760) passed all three jobs, including optimized 32-bit normal and GC-stress execution. Subsequent changes strengthen wide-return/unboxing regressions; their final CI result is recorded in PR #37.

The nullable argument ABI copies aggregate parameters into callee storage. Signature decoding preserves parameter kinds and embedded root offsets; taking an argument’s address never guesses its representation from its consumers. Generic reference fields reserve eight-byte slots so their layouts remain valid on both the portable 64-bit host and GameCube. Reference arrays use native pointer-width elements.

`setjmp`-protected scalar state is volatile. Every active catch retains its own rooted exception object, so an inner catch cannot replace the exception rethrown by an enclosing catch.

### Separate target verification

Hardware or Dolphin execution has not been performed in this work environment. A successful DOL build and 32-bit host run do not verify PowerPC execution, graphics/input/network integration, or total runtime stack high-water usage. The eight Phase 7 acceptance gates above cover compiled host integration, a DOL build and measured static footprint. Hardware/emulator execution remains a separate verification gate; Dolphin emulator CI remains deferred as below. PR #37 has not been merged.

The footprint artifact reports per-function compiler stack usage, not an end-to-end stack bound. The configured GameCube managed heap is 256 KiB. For build `0940fb7`, the linked sample reports text/data/BSS of 219130/46676/496736 bytes; the largest emitted sample frame is 336 bytes and the largest native managed-runtime static frame is 80 bytes. These figures include the linked platform dependencies and must be rechecked after build changes.

## Quality gate

No Phase 7 change merges without green managed, portable runtime and GameCube build jobs. Dolphin emulator CI is explicitly deferred.
