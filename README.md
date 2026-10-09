# DolphinDotNet

**Experimental closed-world C# AOT toolchain and managed runtime for Nintendo GameCube.**

DolphinDotNet compiles ordinary .NET assemblies into native C through a typed intermediate representation, then uses devkitPPC/libogc to produce a GameCube DOL. It does **not** embed CoreCLR or claim general .NET compatibility.

## Current state (October 2026)

| Area | Status |
| --- | --- |
| Managed assembly / CIL import | Implemented for a growing, deliberately constrained subset |
| Production compiler | Typed Value IR, control-flow/stack analysis, native C AOT (`--aot`) |
| Legacy bytecode VM | Retained as a bootstrap/reference path |
| Managed runtime | Compiled object lifecycle, fields, virtual/interface dispatch, strings, arrays, delegates, boxing, typed exceptions, rethrow and finally |
| Memory management | Nonmoving mark/sweep GC, explicit roots and compiler-generated shadow-stack support, reusable free blocks, stress mode |
| GameCube platform | libogc DOL, OpenGX 3D/overlay, PAD input, nonblocking UDP, console overlay |
| Integration | Compiler and native runtime tests; 32/64-bit managed smoke execution, GC stress, ASan/UBSan and GameCube build CI |
| Phase 7 runtime completeness | **Acceptance gates satisfied and merged** in [PR #37](https://github.com/capz/DolphinDotNet/pull/37); target execution remains separate |

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
- `tests/ControlFlowSmoke` and `tests/GenericSharingSmoke` validate compiled managed behavior, including lifecycle, nullable values, generic dispatch, embedded reference fields, exceptions and finally.
- GitHub Actions runs managed integration on 32-bit and 64-bit hosts, collects before every allocation in GC-stress runs, and executes optimized ASan/UBSan checks.
- GameCube CI builds ELF/DOL/map artifacts and publishes section sizes, symbol sizes and per-function static stack usage.
- [Phase 7 CI run 996](https://github.com/capz/DolphinDotNet/actions/runs/37862418540) passed all three jobs before PR #37 merged. See [acceptance evidence](docs/phase7-runtime.md) for coverage and footprint details.
- Automated Dolphin emulator smoke testing (former Phase 6 Stage 11) is **deferred**; a green build does not establish successful execution on emulator or hardware.

## API and architecture rules

GameCube-specific managed APIs belong under the **`Dolphin` namespace** (for example `Dolphin.Graphics`, `Dolphin.Input`, `Dolphin.Network`). Existing `DolphinDotNet.GameCube` API code is legacy and requires migration with compiler intrinsic mapping updates. Standard-library-compatible APIs retain `System.*` names.

The managed/native split keeps game code independent of GX, PAD, libogc and native socket details. The native GameCube backend owns hardware resources.

**Permanently out of scope:** full reflection and dynamic assembly loading. Closed-world static metadata and explicitly supported type checks are allowed.

## Roadmap

| Stage | Status | Scope |
| --- | --- | --- |
| Phase 7 — runtime completeness | Acceptance gates satisfied; merged | Compiled lifecycle, precise GC roots, exceptions/finally, strings/arrays, type operations, generics, portable integration and GameCube build/static footprint |
| Phase 8 — managed platform APIs | Next | Game, graphics, input, networking and diagnostics under `Dolphin.*`; migrate legacy APIs and replace compiler special cases with native-call metadata |
| Phase 9 — integrated managed demo | Planned | Textured 3D, controller interaction, UDP and diagnostic overlay, driven from C# and compiled to a DOL |
| Dolphin / hardware validation | Pending; emulator CI deferred | Verify boot, rendering, input, networking, stability and runtime heap/stack usage on the target |

The **0.1 milestone is still pending**. It requires the integrated managed demo to boot on Dolphin and real GameCube hardware, render a textured 3D object, respond to controller input, exchange UDP packets and display diagnostics without a desktop CLR. Static stack reports do not establish runtime stack high-water usage.

The next library work is equality/comparison, nullable and generic enumerable/collection contracts. Phase 8 establishes a consistent managed platform API over the existing native drivers, followed by the Phase 9 demo.

For ordinary .NET library development, see the [.NET Standard compatibility index](docs/dotnet-standard-status.md). It distinguishes compiled API subsets from native-only primitives and missing framework implementations. The proposed [File and Directory design](docs/system-io-design.md) describes the libogc2/libdvm storage backend and managed `System.IO` rollout.

See [architecture](docs/architecture.md), [managed runtime architecture](docs/managed-runtime-architecture.md), [runtime levels](docs/runtime-levels.md) and [platform roadmap](docs/platform-roadmap.md).
