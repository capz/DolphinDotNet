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
- **Support common asset formats for easy homebrewing**
  - Out of the box support for obj, fbx, glb 3d models with meshes, animations, textures
  - Out of the box support for adx, with looping regions converted from mp3 with sidecart metadata using supplied tooling

## Current focus ##
   - **Supporting live debugging via emulator**
   - Supporting on-device debugging via BBA (LAN)
   - Supporting on-device debugging via USB Gecko (serial port)

## Quick start: build and run Pong

Install the **.NET 8 SDK**, **Dolphin**, and a native build backend:

- **Windows, Linux or macOS:** Docker Desktop/Engine running Linux containers.
  The first build downloads the pinned native toolchain image.
- **Linux or macOS alternative:** devkitPPC and libogc2, with `DEVKITPRO` and
  `DEVKITPPC` configured, plus Make, Bash, Git and Python 3. See the
  [IDE setup guide](docs/ide-workflow.md) for the required libraries.

Clone the independent [DolphinDotNet.Samples repository](https://github.com/capz/DolphinDotNet.Samples) and open `Pong/Pong.sln`:

- **Visual Studio 2022 (17.8+):** select Pong as the startup project, choose the
  **Dolphin Play** launch profile, and press **Ctrl+F5**.
- **Rider with .NET 8 support:** disable **Use ReSharper Build** under
  **Settings → Build, Execution, Deployment → Toolset and Build**, select
  **Pong in Dolphin (play without debugger)**, and click **Run**.
  **Pong in Dolphin** enables GDB and waits for Rider attach.

Or, from the repository root:

```sh
git clone https://github.com/capz/DolphinDotNet.Samples.git
cd DolphinDotNet.Samples
dotnet run --project Pong -c Release
```

Build/Run compiles the managed project, generates native C, builds
`Pong/bin/<configuration>/net8.0/Pong.dol` and `Pong.elf`, and launches Dolphin.
Every build performs the full native build; there is no native incremental
cache. To build without launching, use `dotnet build Pong/Pong.sln`.
To launch the last successful Release build, use `dotnet run --project Pong -c Release --no-build`.

Dolphin is discovered automatically in standard locations or on `PATH`.
For a portable installation, set `DOLPHIN_PATH` to its executable and restart
the IDE. On first launch, configure both controller ports in Dolphin's
**Controllers** dialog. Emulator settings persist in the sample's isolated
`obj/dolphin-user/` directory.

Every [sample](https://github.com/capz/DolphinDotNet.Samples/blob/main/README.md) includes its own `.sln` and Dolphin launch
profiles, using the same workflow.

The [IDE setup guide](docs/ide-workflow.md) covers backend selection, local
settings, troubleshooting and adding the workflow to another project.
For C# source debugging of Debug targets, use the experimental
[Rider attach plugin](docs/rider-debugging.md). Hot Reload is not implemented.
Debug CLI/project launches wait for Rider on port 2166 by default. Use Release
or Rider's **play without debugger** configuration to run without attaching.

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

See the [samples](https://github.com/capz/DolphinDotNet.Samples/blob/main/README.md) for [peer-to-peer kart racing](https://github.com/capz/DolphinDotNet.Samples/blob/main/Kart/README.md), [TEV shader effects](https://github.com/capz/DolphinDotNet.Samples/blob/main/ShaderDemo/README.md), [HTTP PNG textures](https://github.com/capz/DolphinDotNet.Samples/blob/main/HttpTexture/README.md), [Fox](https://github.com/capz/DolphinDotNet.Samples/blob/main/Fox/README.md), [Green Men](https://github.com/capz/DolphinDotNet.Samples/blob/main/GreenMen/README.md), two-player Pong, memory-card Snake, rendering, automatic async networking and
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
dotnet build DolphinDotNet.Samples/HelloGameCube/HelloGameCube.csproj -c Release -p:DndBuildNative=false
dotnet run --project tools/DolphinDotNet.Compiler -- \
  --bundle DolphinDotNet.Samples/HelloGameCube/bin/Release/net8.0/HelloGameCube.dll \
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
  --aot DolphinDotNet.Samples/HelloGameCube/bin/Release/net8.0/HelloGameCube.dll hello.c
```

See [standalone builds](docs/standalone-native-builds.md) for SDK dependencies and
platform targets, [async APIs](docs/async-runtime.md),
[storage](docs/storage-runtime.md), [networking](docs/network-runtime.md),
[ARAM](docs/aram-runtime.md), [models](docs/model-assets-runtime.md),
[timing](docs/timing.md), [audio](docs/audio.md), [controller input](docs/controller-input.md), [diagnostics](docs/debug-overlay.md), and [save image preparation](tools/DolphinDotNet.SaveImage/README.md)
for feature-specific usage and limits.

Raw GX/GU/VIDEO bindings and managed meshes, textures, materials and Draw2D are documented in [Graphics APIs](docs/graphics.md).

External consumers: see the [NuGet SDK guide](docs/nuget-sdk.md) and [release process](docs/releases.md).

Remaining emulator, hardware, IDE and release qualification is tracked in the
[repository-wide manual testing TODO](docs/manual-testing-todo.md).

IDE plugins, the shared debug adapter and their releases now live in [DolphinDotNet.Debuggers](https://github.com/capz/DolphinDotNet.Debuggers). See the [repository boundary and pinned SDK dependency](docs/debugger-components.md).

For on-device launch, [DolphinDotNet.Launcher](tools/DolphinDotNet.Launcher/README.md)
is a standalone GameCube ELF receiver over BBA or USB Gecko, with an IDE readiness
handshake and a validated execution handoff. Its companion debugger mode uploads
an existing Debug ELF, then attaches to the application's waiting GDB stub.
The resident receiver returns to readiness after a normal application exit,
allowing repeated uploads without rebooting the launcher.
