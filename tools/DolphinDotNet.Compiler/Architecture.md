# Compiler architecture

DolphinDotNet compiles a closed world of reachable managed methods to native C.

## Pipeline

`MetadataLoader → MetadataResolver → DependencyGraph → CilDecoder → CFG → stack analysis → typed Value IR → C backend → devkitPPC`

Metadata loading establishes types, fields, method signatures and layouts.
Resolution selects overloads, maps supported standard APIs to managed facades and
specializes generic metadata. Reachability includes virtual methods, type
initializers and async runtime services.

The decoder preserves branch targets, switch tables, metadata tokens, constants
and indexed operands. CFG construction records predecessors and successors.
Stack analysis validates block entry/exit states. Value IR represents typed values,
mutable locals and arguments, calls, objects, fields, phi joins and control flow.
The verifier checks the IR before C generation.

Phi values become predecessor-edge assignments. The backend emits native C with
explicit GC roots and exception regions; GCC performs machine optimization.

## Identity and intrinsics

Method identity includes assembly, declaring type, name and metadata signature.
Generated symbols derive from that identity. Unique methods also receive aliases
used by characterization tests.

`IntrinsicRegistry` and native bindings connect supported managed APIs to the
runtime ABI. Game code uses `System.*` and `Dolphin.*` APIs. Samples own their scene
composition, geometry and application loops.

## Runtime boundary

Generated code targets `dnd_managed.h` and platform headers. The runtime provides
allocation, GC, type operations, exception state and async executor management.
The native bootstrap initializes platform resources, invokes the managed entry
point and joins the async service before releasing application roots.

Unsupported managed calls fail during compilation. The supported subset and its
validation limits are recorded in the [API inventory](../../docs/dotnet-standard-status.md).
