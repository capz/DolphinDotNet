# DolphinDotNet

**Experimental closed-world C# AOT toolchain and managed runtime for Nintendo GameCube.**

DolphinDotNet compiles ordinary .NET assemblies into native C through a typed intermediate representation, then uses devkitPPC/libogc to produce a GameCube DOL. It does **not** embed CoreCLR or claim general .NET compatibility.

## Current state (October 2026)

| Area | Status |
| --- | --- |
| Managed assembly / CIL import | Implemented for a growing, deliberately constrained subset |
| Production compiler | Typed Value IR, control-flow/stack analysis, native C AOT (`--aot`) |
| Legacy bytecode VM | Retained as a bootstrap/reference path |
| Managed runtime | Objects, type metadata, fields, virtual/interface dispatch primitives, strings, arrays, delegates, boxing, exception state |
| Memory management | Nonmoving mark/sweep GC, explicit roots and compiler-generated shadow-stack support, reusable free blocks, stress mode |
| GameCube platform | libogc DOL, OpenGX 3D/overlay, PAD input, nonblocking UDP, console overlay |
| Integration | Compiler, portable native-runtime, and GameCube build CI; broader C# runtime semantics still being verified |
| Phase 7 runtime completeness | **In progress** — see [Phase 7 acceptance gates](docs/phase7-runtime.md) |

The native runtime implementing a feature does **not** automatically mean all corresponding C# constructs are fully supported by the AOT compiler.

## Build pipeline

```text
C# source -> Roslyn / .NET assembly -> CIL + metadata
  -> CFG / stack analysis -> typed Value IR -> generated native C
  -> devkitPPC + libogc -> ELF -> DolphinDotNet.dol
```

`--aot` is the production compiler path; `--value-aot` is its explicit alias. `--legacy-aot` is retained for regression characterization. The original bytecode compiler is not the production target.

## Try the compiler

Requires the .NET SDK (see project target frameworks) and, for DOL builds, devkitPro with devkitPPC, libogc and the GameCube OpenGX port.

```sh
dotnet run --project tools/DolphinDotNet.Compiler -- --aot path/to/Managed.dll generated/generated_program.c
make
```

The output is `DolphinDotNet.dol`. The repository also contains `samples/HelloGameCube`, `samples/ManagedGameCube`, and `scripts/compile-sample.sh`.

## Tests and CI

- `tests/managed_runtime_tests.c` covers native managed-runtime mechanisms, GC, UTF-16 strings, arrays, type checks and exceptions.
- `tools/DolphinDotNet.Compiler.Tests` exercises compiler lowering.
- GitHub Actions builds managed samples, portable native runtime tests and the GameCube target.
- Automated Dolphin emulator smoke testing (former Phase 6 Stage 11) is **deferred**; a green build does not establish successful execution on emulator or hardware.

## API and architecture rules

GameCube-specific managed APIs belong under the **`Dolphin` namespace** (for example `Dolphin.Graphics`, `Dolphin.Input`, `Dolphin.Network`). Existing `DolphinDotNet.GameCube` API code is legacy and requires migration with compiler intrinsic mapping updates. Standard-library-compatible APIs retain `System.*` names.

The managed/native split keeps game code independent of GX, PAD, libogc and native socket details. The native GameCube backend owns hardware resources.

**Permanently out of scope:** full reflection and dynamic assembly loading. Closed-world static metadata and explicitly supported type checks are allowed.

## Roadmap

1. **Phase 7 — runtime completeness:** validate compiled C# object lifecycle, precise GC roots, exceptions/finally, strings/arrays, type system, generics and integrated GameCube constraints.
2. **Phase 8 — managed platform APIs:** Game, graphics, input, network and diagnostics under `Dolphin.*`.
3. **Phase 9 — integrated demo:** textured 3D, controller interaction, UDP and diagnostic overlay, compiled from C# to a DOL.
4. **Hardware validation:** test the actual DOL on Dolphin and GameCube; emulator CI remains a separate deferred task.

See [architecture](docs/architecture.md), [managed runtime architecture](docs/managed-runtime-architecture.md), [runtime levels](docs/runtime-levels.md) and [platform roadmap](docs/platform-roadmap.md).
