# Compiler architecture

DolphinDotNet is a small closed-world AOT .NET environment. The production compiler deliberately separates raw CIL decoding from runtime semantics.

## Pipeline

`MetadataLoader -> DependencyGraph -> CilDecoder -> CFG -> stack analysis -> typed Value IR -> C backend -> devkitPPC`

The decoder preserves typed operands including branch targets, switch tables, metadata tokens, integer constants, and indexed variable operands. CFG construction records predecessors and successors. Stack analysis validates block entry/exit states and supplies stack kinds. Value IR consumes the CIL evaluation stack and represents values, mutable locals/arguments, calls, objects/fields, phi joins, branches, switches, and returns explicitly.

Phi values are lowered to predecessor-edge assignments. The C backend emits ordinary labels/gotos/switches and leaves machine optimization to GCC.

## Identity and intrinsics

Method identity includes assembly, declaring type, name, and the metadata signature blob so overloads no longer collide. Generated Value AOT symbols derive from that identity. Unique methods also receive compatibility aliases used by characterization tests.

Platform substitutions are classified by `IntrinsicRegistry`. Current intrinsics cover string length plus GameCube console output, controller input, and demo-frame presentation. Managed application code does not reference libogc or runtime C structures.

## Runtime boundary

Generated code targets `dnd_managed.h` and platform headers. Object allocation and type metadata remain runtime responsibilities. C is not part of the managed application contract.

The GameCube sample now owns its frame loop in managed C#: it polls controller input, exits on Start, advances managed state, and invokes a frame-presentation intrinsic. Native `main` performs platform/runtime initialization and then hands control to managed `Main`.

## Remaining runtime work beyond this compiler milestone

The typed compiler architecture is in place, but broad .NET compatibility still requires precise GC maps and tracing, virtual/interface dispatch, generic instantiation, exception regions, richer value types and floating-point semantics, static initialization, arrays, delegates, boxing, and the selected .NET Standard BCL surface. Those are runtime/conformance milestones rather than reasons to retain the old stack-oriented compiler.
