# DolphinDotNet.Compiler

DolphinDotNet uses a closed-world AOT pipeline for GameCube builds:

```
C# -> Roslyn CIL -> CIL decoder -> CFG/stack analysis -> typed Value IR
   -> native C -> devkitPPC GCC -> ELF -> DOL
```

`--aot` is the production typed-IR path. `--value-aot` is retained as an explicit alias for compiler characterization, while `--legacy-aot` keeps the previous stack-oriented C backend available as a regression oracle. The original no-flag DND bytecode compiler remains a bootstrap/reference VM path.

The typed path models CFG fallthrough, loops, conditional branches, switch, stack joins/phi values, mutable locals and arguments, signed/unsigned comparisons, direct managed calls, overload-aware method identity, object construction, instance fields, managed strings, and registered GameCube intrinsics. Intrinsics are centralized in `IntrinsicRegistry` rather than recognized ad hoc by the importer.

Usage:

```
dotnet run --project tools/DolphinDotNet.Compiler -- --aot Managed.dll generated_program.c
```

C remains an implementation detail. Managed applications target DolphinDotNet managed APIs; generated C targets the runtime/platform ABI. Unsupported semantics should fail compilation rather than silently change managed behavior.
