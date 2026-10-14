# DolphinDotNet

**Experimental closed-world C# AOT toolchain and managed runtime for Nintendo GameCube.**

DolphinDotNet compiles ordinary .NET assemblies into native C through a typed intermediate representation, then uses devkitPPC/libogc2 to produce a GameCube DOL. It does **not** embed CoreCLR or claim general .NET compatibility.

## Current state (October 2026)

| Area | Status |
| --- | --- |
| Managed assembly / CIL import | Implemented for a growing, deliberately constrained subset |
| Production compiler | Typed Value IR, control-flow/stack analysis, native C AOT (`--aot`) |
| Legacy bytecode VM | Retained as a bootstrap/reference path |
| Managed runtime | Compiled object lifecycle, fields, virtual/interface dispatch, strings, arrays, delegates, boxing, typed exceptions, rethrow and finally |
| Memory management | Nonmoving mark/sweep GC, explicit roots and compiler-generated shadow-stack support, reusable free blocks, stress mode |
| GameCube platform | libogc2 DOL, OpenGX 3D/overlay, PAD input, nonblocking UDP/TCP client/server, console overlay |
| Collections and storage | Selected standard List/comparers, streams/text and System.IO APIs; SD/DVD mounting and separate memory-card saves |
| Async/await | Compiled Task/Task<T> state machines, cancellation and cooperative continuations; runtime-owned service thread and captured continuations |
| Integration | Compiler and native runtime tests; 32/64-bit managed smoke execution, GC stress, ASan/UBSan and GameCube build CI |
| Phase 7 runtime completeness | **Acceptance gates satisfied and merged** in [PR #37](https://github.com/capz/DolphinDotNet/pull/37); target execution remains separate |

The native runtime implementing a feature does **not** automatically mean all corresponding C# constructs are fully supported by the AOT compiler.

The [networking runtime](docs/network-runtime.md) adds selected standard `System.Net`, socket and HTTP APIs, DNS, streamed HTTP/1.1 downloads and a bounded JSON reader. Networking and continuations advance automatically through runtime-owned dispatch. Verified HTTPS/TLS and hardware networking acceptance remain pending.

## Build pipeline

```text
C# source -> Roslyn / .NET assembly -> CIL + metadata
  -> CFG / stack analysis -> typed Value IR -> generated native C
  -> devkitPPC + libogc2 -> ELF -> DolphinDotNet.dol
```

`--aot` is the production compiler path; `--value-aot` is its explicit alias. `--legacy-aot` is retained for regression characterization. The original bytecode compiler is not the production target.

## Try the compiler

Requires the .NET SDK (see project target frameworks) and, for DOL builds, devkitPro with devkitPPC and the compatible libogc2 GameCube packages (OpenGX, FAT/libdvm and ISO9660). CI pins the toolchain image.

```sh
dotnet run --project tools/DolphinDotNet.Compiler -- --aot path/to/Managed.dll generated/generated_program.c
make
```

The output is `DolphinDotNet.dol`. See the [sample guide](samples/README.md) for all four examples and standalone AOT/DOL build commands. `sh scripts/compile-sample.sh` generates a standalone HelloGameCube bundle in `out/HelloGameCube`.

The [async networking sample](samples/AsyncNetworking/Program.cs) awaits platform startup and a DNS-resolved HTTP JSON request using an ordinary async Main without application pump calls. Its defaults target the local test fixture; configure a reachable resolver and endpoint for hardware.

## Tests and CI

- `tests/managed_runtime_tests.c` covers native managed-runtime mechanisms, GC, UTF-16 strings, arrays, type checks and exceptions.
- `tools/DolphinDotNet.Compiler.Tests` exercises compiler lowering.
- `tests/AsyncSmoke` covers nested awaits, bounded scheduler pumping, retained state, faults/finally, cancellation, completion sources, WhenAll/WhenAny and System/Dolphin await interoperability.
- `tests/ControlFlowSmoke` and `tests/GenericSharingSmoke` validate compiled managed behavior, including lifecycle, nullable values, generic dispatch, embedded reference fields, exceptions and finally.
- GitHub Actions runs managed integration on 32-bit and 64-bit hosts, collects before every allocation in GC-stress runs, and executes optimized ASan/UBSan checks.
- GameCube CI builds ELF/DOL/map artifacts and publishes section sizes, symbol sizes and per-function static stack usage.
- [Phase 7 CI run 996](https://github.com/capz/DolphinDotNet/actions/runs/37862418540) passed all three jobs before PR #37 merged. See [acceptance evidence](docs/phase7-runtime.md) for coverage and footprint details.
- Automated Dolphin emulator smoke testing (former Phase 6 Stage 11) is **deferred**; a green build does not establish successful execution on emulator or hardware.

## API and architecture rules

GameCube-specific managed APIs belong under the **`Dolphin` namespace** (for example `Dolphin.Graphics`, `Dolphin.Input`, `Dolphin.Network`). The former `DolphinDotNet.GameCube.GameCube` facade is replaced by `Dolphin.Diagnostics.DebugOverlay`, `Dolphin.Input.Controller`, `Dolphin.Graphics.Renderer` and `Dolphin.Platform.Name`. The assembly/project name remains `DolphinDotNet.GameCube`. Application code uses supported standard-library APIs through `System.*` names. Their Core implementations live under `Dolphin.*` and are redirected during AOT compilation; they are not interchangeable CLR types. Import the scheduler with `using Scheduler = Dolphin.Threading.Tasks.Scheduler;` alongside `System.Threading.Tasks`. See [async scope and namespace rules](docs/async-runtime.md).

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

Further library work includes general equality/comparison, nullable coverage, standard List mapping and broader collection implementations. Phase 8 establishes a consistent managed platform API over the existing native drivers, followed by the Phase 9 demo.

For ordinary .NET library development, see the [.NET Standard compatibility index](docs/dotnet-standard-status.md). It distinguishes compiled API subsets from native-only primitives and missing framework implementations. The [supported storage APIs](docs/storage-runtime.md) document usage and limits; the [File and Directory design](docs/system-io-design.md) describes the libogc2/libdvm storage backend and managed `System.IO` rollout.

See [architecture](docs/architecture.md), [managed runtime architecture](docs/managed-runtime-architecture.md), [runtime levels](docs/runtime-levels.md) and [platform roadmap](docs/platform-roadmap.md).

## Save icon preparation

Convert a PNG to a 32×32 tiled RGB5A3 memory-card icon:

```sh
dotnet run --project tools/DolphinDotNet.SaveImage -- icon.png icon.rgb5a3
```

Pass `--source` to embed bytes in a generated C# file, `--banner` for 96×32 memory-card banners, or `--disc-banner` to generate a disc `opening.bnr` with game metadata. See the [converter instructions](tools/DolphinDotNet.SaveImage/README.md) for resize modes, .NET tool installation and loading the bytes into `MemoryCardSaveOptions`.

## ARAM cold-asset storage

`Dolphin.Memory.Aram` provides disposable binary allocations and cooperative queued transfers into auxiliary RAM. `AramAssetCache` adds priority/LRU eviction and range prefetch. Initialize ownership/reservations before other ARAM users; managed objects remain in main RAM. See [ARAM runtime](docs/aram-runtime.md) for lifecycle, budgets and hardware acceptance limits.

### Model assets, animation and residency

`DolphinDotNet.Assets` compiles FBX, OBJ/MTL, glTF and GLB into self-contained GameCube model packages with GX textures, materials, bone weights and named animation clips. `Dolphin.Graphics.Model` loads cooperatively; independent `ModelInstance`/`GameObject` objects provide playback, cached transforms and one-call drawing. Models can unload their large buffers to ARAM and restore without rereading storage. See [model asset runtime](docs/model-assets-runtime.md) for usage, budgets, supported features and hardware validation limits.

### Performance overlay

Set `Dolphin.Diagnostics.DebugOverlay.PerformanceVisible = true` to show FPS, frame time,
submitted scene triangles and main/auxiliary RAM use in the bottom-left corner. The panel
uses light gray text on black, is disabled by default, and can be hidden at runtime by setting
the property to `false`. See [debug overlay](docs/debug-overlay.md) for metric definitions.

### Managed source layout and sample rendering

Assembly projects stay in `src/DolphinDotNet.Core` and `src/DolphinDotNet.GameCube`.
Within each project, source paths follow the complete declared namespace, for example:

- `Dolphin/Graphics/Renderer.cs` → `Dolphin.Graphics`
- `Dolphin/Network/Http/Http.cs` → `Dolphin.Network.Http`
- `Dolphin/Serialization/Json/JsonReader.cs` → `Dolphin.Serialization.Json`
- `Dolphin/Text/Encoding.cs` → `Dolphin.Text`
- `Dolphin/Threading/Tasks/Tasks.cs` → `Dolphin.Threading.Tasks`
- `Dolphin/Runtime/CompilerServices/Awaitables.cs` → `Dolphin.Runtime.CompilerServices`

`ManagedGameCube/DemoScene.cs` owns `PresentDemoFrame`: its background colour,
projection, rotation, vertex colours and triangle geometry. The framework provides
reusable `Dolphin.Graphics.ImmediateRenderer` drawing operations and handles the
diagnostic overlay and presentation in `EndFrame()`. `Renderer` remains the
model-rendering API. The compiler binds generic native operations; it contains no
demo scene intrinsic.
