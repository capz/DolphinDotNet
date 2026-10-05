# Runtime compatibility levels

## Level 1 - C# runtime

Initial native primitives cover object/type metadata, inheritance/interface checks, UTF-16 managed strings, checked one-dimensional arrays, delegates, runtime exception state, root registration and a first-phase managed heap collector.

This is infrastructure rather than a claim of complete C# support. The compiler must still lower construction, fields, virtual/interface dispatch, generics, exception regions, delegates, arrays and roots into these primitives.

The collector intentionally does not compact live objects or reclaim individual unreachable objects yet. Until compiler-emitted reference maps exist, it resets the bump heap only when no registered roots remain.

## Level 2 - core BCL

Initial primitives cover System.Object/System.String/System.Array representation, integer Math Abs/Min/Max, Random, and growable List-style storage.

These are targets for managed facades/intrinsics. Most BCL algorithms should ultimately be managed C# rather than duplicated in C.

## Completion criteria

Level 1 is complete when ordinary compiled C# exercises classes, structs, interfaces, generics, arrays, strings, exceptions, delegates and GC through the compiler.

Level 2 is complete when managed implementations of collections, delegates, nullable, StringBuilder, basic LINQ, streams/readers/writers, Math and Random compile and execute through Level 1.

Formal .NET Standard conformance is a later level.
