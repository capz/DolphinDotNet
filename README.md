# DolphinDotNet

DolphinDotNet is an experimental tiny managed runtime and C# toolchain for Nintendo GameCube, built on devkitPPC/libogc.

## Current milestone: real C# input

The repository now accepts an ordinary compiled .NET assembly for a deliberately small C# subset. The host-side `dndc` compiler reads PE/CLI metadata and CIL using `System.Reflection.Metadata`, translates the managed entry point to DolphinDotNet bytecode, and emits a C header embedded into the GameCube executable.

The included sample is ordinary C#:

```csharp
Console.WriteLine("Hello from real C#!");
Console.WriteLine("Running through DolphinDotNet.");
Console.WriteLine(40 + 2);
```

The pipeline is:

```
C# -> dotnet/Roslyn -> .NET DLL -> dndc -> DND bytecode/C data
   -> devkitPPC + libogc -> DolphinDotNet.dol
```

This is **not yet a conforming .NET Standard implementation**. It is the first vertical slice proving that normal C# compiler output can feed the GameCube runtime.

## Supported CIL subset

- `nop`
- `ldstr`
- `ldc.i4.m1`, `ldc.i4.0` ... `ldc.i4.8`, `ldc.i4.s`, `ldc.i4`
- `add`, `sub`, `mul`, `div`
- `pop`
- `ret`
- `call System.Console.WriteLine(string)`
- `call System.Console.WriteLine(int)`

Unsupported opcodes and calls fail during compilation with the CIL offset rather than silently producing a broken DOL.

## Compile the C# sample

Requires .NET 8:

```sh
sh scripts/compile-sample.sh
```

This builds `samples/HelloGameCube` and regenerates `generated/generated_program.h`.

## Build for GameCube

Install devkitPro with the GameCube development packages and make sure `DEVKITPPC` is set:

```sh
make
```

The output is `DolphinDotNet.dol`.

## Runtime

The GameCube side currently provides:

- compact object headers/type IDs
- 256 KiB prototype managed heap
- managed UTF-8 strings
- one-dimensional managed arrays
- compact stack VM
- native/internal-call bridge to libogc
- GameCube console output and controller exit handling

The portable runtime has desktop-hosted C tests, while CI separately compiles the real C# sample and verifies its generated source.

## Next milestones

1. Locals and arguments
2. Conditional/unconditional branches and loops
3. Calls between user-defined managed methods
4. Static/instance fields and constructors
5. Arrays and richer strings from C#
6. Offline type layout/metadata tables
7. Replace bytecode interpretation with PowerPC AOT
8. Exceptions and a simple tracing/mark-sweep GC
9. Grow a small useful BCL surface

See `docs/architecture.md` for the longer-term design.
