# DolphinDotNet

An experimental C# AOT toolchain and managed runtime for Nintendo GameCube.
DolphinDotNet compiles .NET assemblies to native C, then builds a GameCube DOL
with devkitPPC and libogc2. Games run without a desktop CLR.

## Project goals ##

- **Make network requests using HttpClient**
  - Using standard .net httpclient, no platform-specific code
  - Allowing for file downloads and simple client-server communications
- **Provide for a full-featured development environment for writing apps and games using dotnet and C#**
  - Compiles Ahead-of-Time, into devkitppc+libogc2 C/++ code, then uses devkitppc to compile into a functional dol executable
  - Allows for on-device debugging using visual studio and rider, using gdb over BBA or USB Gecko
  - **Support many assets from retail games**
  - Out of the box support for meshes, textures, animations, sound files
- **Support some common asset formats*
  - Out of the box support for obj, fbx, glb 3d models with meshes, animations, textures
  - Out of the box support for adx, with looping regions converted from mp3 with sidecart metadata using supplied tooling

## Quick start: build and run Pong

Install the **.NET 8 SDK**, **Dolphin**, and a native build backend:

- **Windows, Linux or macOS:** Docker Desktop/Engine running Linux containers.
  The first build downloads the pinned toolchain image used by CI.
- **Linux or macOS alternative:** devkitPPC and libogc2, with `DEVKITPRO` and
  `DEVKITPPC` configured, plus Make, Bash, Git and Python 3. See the
  [IDE setup guide](docs/ide-workflow.md) for the required libraries.

Open [`samples/Pong/Pong.sln`](samples/Pong/Pong.sln):

- **Visual Studio 2022 (17.8+):** select Pong as the startup project, choose the
  **Dolphin** launch profile, and press **Ctrl+F5**.
- **Rider with .NET 8 support:** disable **Use ReSharper Build** under
  **Settings → Build, Execution, Deployment → Toolset and Build**, select
  **Pong in Dolphin**, and click **Run**.

Or, from the repository root:

```sh
dotnet run --project samples/Pong
```

Build/Run compiles the managed project, generates native C, builds
`samples/Pong/bin/Debug/net8.0/Pong.dol` and `Pong.elf`, and launches Dolphin.
Every build performs the full native build; there is no native incremental
cache. To build without launching, use `dotnet build samples/Pong/Pong.sln`.
To launch the last successful build, use `dotnet run --project samples/Pong --no-build`.

Dolphin is discovered automatically in standard locations or on `PATH`.
For a portable installation, set `DOLPHIN_PATH` to its executable and restart
the IDE. On first launch, configure both controller ports in Dolphin's
**Controllers** dialog. Emulator settings persist in the sample's isolated
`obj/dolphin-user/` directory.

Every [sample](samples/README.md) includes its own `.sln` and Dolphin launch
profiles, using the same workflow.

The [IDE setup guide](docs/ide-workflow.md) covers backend selection, local
settings, troubleshooting and adding the workflow to another project.
Running in Dolphin supports native execution; C# source debugging and Hot Reload
are not implemented.

## Features

- Managed objects, fields, virtual/interface dispatch, generics, delegates,
  strings, arrays, boxing, typed exceptions and `try`/`catch`/`finally`.
- Nonmoving mark/sweep garbage collection with compiler-generated roots.
- Supported standard `System.*` APIs for lists and comparers, binary/text streams,
  files and directories, encoding, tasks and cancellation.
- Monotonic clocks, frame timing, async delays and disposable callback timers.
- Ordinary `async`/`await`, `Task.Run` and captured continuations, dispatched by
  a runtime-owned service thread. Managed execution is serialized between the
  application and background executors.
- PCM WAV audio with libogc2 AESND, overlapping voices, clip looping, volume and pitch controls.
- GameCube graphics, controller input, diagnostic text and performance metrics
  through `Dolphin.*` APIs.
- Model conversion, GX materials/textures, animation, transforms and cameras.
- ARAM binary storage, streaming, asset caching and model residency.
- IPv4 UDP/TCP, DNS, streamed HTTP/1.1 and bounded JSON parsing.
- SD/DVD storage, memory-card saves, and icon/banner preparation tools.

## Scope

Compilation is closed-world: reachable methods and static metadata must fit the
[supported API subset](docs/dotnet-standard-status.md). A .NET target framework
provides compile-time references, not full runtime compatibility. Full reflection,
dynamic assembly loading, TLS/HTTPS, `ValueTask`, general threading
and `System.Text.Json` are outside the implemented surface.

GameCube is the primary target. Wii bundle generation is experimental; Wii U has
only a minimal runtime and requires platform adapters for hardware features.
CI checks host execution, GC stress, sanitizers, GameCube builds and Dolphin boot
and task execution. Physical hardware acceptance, including network and ARAM
operation, remains incomplete.

## Minimal example

Reference `src/DolphinDotNet.GameCube/DolphinDotNet.GameCube.csproj` from a .NET 8
executable project:

```csharp
using Dolphin.Diagnostics;

internal static class Program
{
    private static int Main()
    {
        DebugOverlay.WriteLine("Hello from C# on GameCube");
        return 0;
    }
}
```

See the [samples](samples/README.md) for two-player Pong, memory-card Snake, rendering, automatic async networking and
HTTP-to-ARAM streaming. Native platform APIs execute in the compiled console
program. Every sample integrates the shared SDK so `dotnet run` builds the DOL
and launches it in Dolphin.

## Manual compilation

The pipeline is C# → Roslyn assembly/CIL → control-flow and stack analysis → typed
Value IR → native C → devkitPPC ELF → DOL.

Install the .NET 8 SDK and devkitPro's devkitPPC/libogc2 GameCube toolchain, including
native GX, FAT/libdvm and ISO9660. Configure `DEVKITPRO` and `DEVKITPPC` for the SDK.
From the repository root:

```sh
dotnet build samples/HelloGameCube/HelloGameCube.csproj -c Release -p:DndBuildNative=false
dotnet run --project tools/DolphinDotNet.Compiler -- \
  --bundle samples/HelloGameCube/bin/Release/net8.0/HelloGameCube.dll \
  out/HelloGameCube --platform gamecube
make -C out/HelloGameCube
```

The bundle includes generated C, native sources/headers, dependency information
and a Makefile. It produces `Game.dol` in the bundle directory; load it in Dolphin
or on a GameCube. Replace the assembly and output paths to compile your own game.
The compiler resolves referenced DolphinDotNet assemblies beside the input DLL.

To emit C without packaging the native build:

```sh
dotnet run --project tools/DolphinDotNet.Compiler -- \
  --aot samples/HelloGameCube/bin/Release/net8.0/HelloGameCube.dll hello.c
```

See [standalone builds](docs/standalone-native-builds.md) for SDK dependencies and
platform targets, [async APIs](docs/async-runtime.md),
[storage](docs/storage-runtime.md), [networking](docs/network-runtime.md),
[ARAM](docs/aram-runtime.md), [models](docs/model-assets-runtime.md),
[timing](docs/timing.md), [audio](docs/audio.md), [controller input](docs/controller-input.md), [diagnostics](docs/debug-overlay.md), and [save image preparation](tools/DolphinDotNet.SaveImage/README.md)
for feature-specific usage and limits.

Raw GX/GU/VIDEO bindings and managed meshes, textures, materials and Draw2D are documented in [Graphics APIs](docs/graphics.md).
