# DolphinDotNet

DolphinDotNet is an experimental tiny managed runtime for Nintendo GameCube, built on devkitPPC/libogc.

The first milestone is intentionally small: prove that a managed-style runtime can live comfortably inside a normal GameCube homebrew executable before attempting a larger .NET surface.

## Current MVP

- GameCube executable built with devkitPPC/libogc
- compact object header and runtime type metadata
- bump-allocated managed heap with deterministic reset
- managed UTF-8 strings
- one-dimensional managed arrays
- tiny stack-based IL-like interpreter
- internal calls from managed bytecode into the native GameCube host
- sample program that prints through the runtime
- desktop-hosted tests for the portable runtime core
- GitHub Actions build using devkitPro

This is **not yet a conforming .NET Standard implementation**. The target is a useful subset that can eventually consume a constrained C#/.NET assembly through AOT translation.

## Build for GameCube

Install devkitPro with the GameCube development packages and make sure `DEVKITPPC` is set, then:

```sh
make
```

The output is `build/DolphinDotNet.dol`.

## Runtime model

The runtime core is ordinary freestanding-friendly C. `source/platform_gamecube.c` is the only libogc-specific layer. Managed programs are currently represented as compact bytecode; this lets us establish object layout, calls, allocation and platform services before adding an ECMA-335 assembly reader/AOT compiler.

### Bytecode implemented

`NOP`, `LDC_I4`, `ADD`, `SUB`, `MUL`, `DIV`, `CALL_INTERNAL`, `POP`, `RET`.

## Roadmap

1. Runtime/bootstrap (this commit)
2. Metadata + richer primitive/object model
3. Offline .NET assembly reader and validation
4. IL -> PowerPC AOT compiler
5. Minimal BCL: System.Object, String, Array, Console, Math and collections
6. Exceptions and basic GC
7. Grow API compatibility based on real GameCube applications

The design deliberately avoids pretending that full CoreCLR is practical on a 24 MB GameCube. The likely useful end state is a constrained AOT .NET profile with familiar C# semantics and selected .NET Standard-compatible APIs.
