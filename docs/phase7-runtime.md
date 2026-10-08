# Phase 7 — Runtime completeness

Status: **in progress**. Runtime completeness means the constrained C# execution model needed for GameCube games, **not** complete desktop .NET compatibility.

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

## Remaining acceptance gates

1. **Object lifecycle:** validate generated constructors, instance/static fields and inheritance through compiled C#.
2. **GC:** ensure every compiler-generated live reference has a precise root; protect temporary allocation inputs; verify pending exceptions, deep graphs, cycles, fragmentation, OOM and constrained-heap behavior.
3. **Exceptions:** verify nested try/catch/finally, rethrow, cleanup and GC-root restoration through compiled C#.
4. **Arrays/strings:** validate overflow, null, bounds, covariance, UTF-16 semantics and allocation failures.
5. **Type system:** test virtual/interface dispatch, casting, boxing and value-type field tracing.
6. **Generics:** validate reference sharing, value specialization and generic dispatch.
7. **Integration:** run a compiled managed smoke application combining the above on the portable host.
8. **GameCube:** produce a DOL with the runtime and measure heap/stack footprint; hardware or emulator execution remains a separate verification gate.

## Current work

The first hardening change roots pending managed exceptions across collection and rejects oversized allocation requests. Further compiler-to-runtime integration and tests are required before declaring Phase 7 complete.

## Quality gate

No Phase 7 change merges without green managed, portable runtime and GameCube build jobs. Dolphin emulator CI is explicitly deferred.
