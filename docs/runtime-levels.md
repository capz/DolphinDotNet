# Runtime compatibility levels

## Level 1 - C# runtime

Initial native primitives cover object/type metadata, inheritance/interface checks, UTF-16 managed strings, checked one-dimensional arrays, delegates, runtime exception state, root registration and a first-phase managed heap collector.

This is infrastructure rather than a claim of complete C# support. The compiler must still lower construction, fields, virtual/interface dispatch, generics, exception regions, delegates, arrays and roots into these primitives.

The collector is non-moving mark/sweep: it reclaims individual unreachable objects, coalesces adjacent free blocks and reuses freed space. Precise tracing uses type reference-offset metadata and compiler-emitted shadow-stack frames. Root correctness under allocation stress remains an explicit Phase 7 acceptance gate.

## Level 2 - core BCL

Initial primitives cover System.Object/System.String/System.Array representation, integer Math Abs/Min/Max, Random, and growable List-style storage.

These are targets for managed facades/intrinsics. Most BCL algorithms should ultimately be managed C# rather than duplicated in C.

## Completion criteria

Level 1 is complete when ordinary compiled C# exercises classes, structs, interfaces, generics, arrays, strings, exceptions, delegates and GC through the compiler.

Level 2 is complete when managed implementations of collections, delegates, nullable, StringBuilder, basic LINQ, streams/readers/writers, Math and Random compile and execute through Level 1.

Selected .NET Standard API compatibility may be tracked, but full reflection and dynamic assembly loading are permanently out of scope. Formal full .NET Standard conformance is not a project goal.
