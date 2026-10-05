# Managed runtime architecture

DolphinDotNet is a closed-world AOT runtime. The compiler owns information that would normally be rediscovered by a JIT/runtime and emits compact metadata for the GameCube target.

## Layers

```
managed game / managed framework
        |
        v
Roslyn CIL
        |
        v
decoder -> CFG -> stack analysis -> typed Value IR
        |
        +---- closed-world dependency/type analysis
        |
        v
runtime metadata + native C
        |
        v
devkitPPC / libogc -> ELF -> DOL
```

Managed applications do not depend on C runtime structures or libogc.

## Object and heap model

A managed object contains only a type pointer and a 32-bit GC word. On the 32-bit GameCube target this is an 8-byte object header. Allocator bookkeeping lives in private heap block headers before managed objects, rather than inflating every managed object.

The managed heap is non-moving mark/sweep with reusable free blocks. Allocation first reuses a suitable free block, then grows the heap, and triggers collection before reporting out-of-memory.

## Precise tracing

`DndType` contains compiler-generated reference offsets. The collector walks those maps through the complete base-type chain. Arrays record whether their elements are object references and may carry element type metadata for value-type tracing.

Generated methods publish only known `ObjectReference` values through shadow-stack frames. Static reference fields are rooted for the lifetime of managed `Main`. The collector does not conservatively interpret arbitrary integers as pointers.

## Dispatch

`DndType` has compact vtable and interface-map slots. The runtime exposes uniform managed-method dispatch helpers. The compiler closes the dependency graph over virtual implementations reachable through instantiated types and emits wrapper thunks and vtables. Direct calls remain direct native calls.

## Arrays, statics, generics, and boxing

The typed AOT path models one-dimensional arrays, checked length/index access, reference-array tracing, static storage, guarded type initializers, generic method specifications, and Int32 boxing/unboxing. Generic method bodies are currently shared when their emitted representation is compatible; representation-specialized generic value types remain a later extension.

## Compatibility direction

The native runtime is intentionally limited to mechanisms: allocation/GC, type identity and dispatch, array primitives, boxing primitives, exception state, delegates, and platform services. Collections, LINQ, streams, formatting, and other framework algorithms should be managed C# as the core library grows.

The legacy stack-oriented backend is no longer part of production CI. `--aot` uses typed Value IR; the old backend remains only as a diagnostic/regression aid while it is useful.
