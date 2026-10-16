# DolphinDotNet samples

Run commands from the repository root. All examples target .NET 8 for compilation;
the GameCube runs generated native code, using only the supported AOT API subset.

| Sample | Demonstrates | Setup / result |
| --- | --- | --- |
| [HelloGameCube](HelloGameCube/Program.cs) | `int Main`, `DebugOverlay.WriteLine`, integer arithmetic and standard `Int32.ToString` | Prints a greeting and 42, then exits |
| [ManagedGameCube](ManagedGameCube/Program.cs) | Cached controller snapshots, debug text and `ImmediateRenderer`; the scene belongs to `DemoScene`, not the framework | Rotating triangle; press Start on controller port 0 to exit |
| [AsyncNetworking](AsyncNetworking/Program.cs) | Standard `async Task<int> Main`, `HttpClient`, `Task.Run` and `ConfigureAwait(false)`; bounded Dolphin JSON parsing | Local DNS/HTTP fixture defaults; returns 0 when JSON `value` is 42, otherwise 1 |
| [HardwareDiagnostics](HardwareDiagnostics/Program.cs) | Safe/extended GameCube device scanning, presence status, physical reset button, controlled reboot | Controller 1 A scans, Start+X restarts, Start+Y requests menu |
| [DiscBrowser](DiscBrowser/README.md) | GC Loader SD mount, read-only GameCube ISO FST, original-game FST on DVD interface | GC Loader-compatible firmware or original DVD/ODE optional; skips unavailable devices |
| [Snake](Snake/README.md) | Classic Snake with Continue/New Game title menu, checked native memory-card saves and two-minute autosave | GameCube memory card A or B recommended; works without card but cannot persist |
| [Pong](Pong/README.md) | Complete two-player Pong: controller input, delta-time physics, 2D rectangles, generated PCM8 sounds | Self-contained GameCube DOL; two controllers, no storage/asset files |
| [AudioDemo](AudioDemo/Program.cs) | AESND WAV effects and looping PCM music with async loading | Mount an SD device containing `audio/laser.wav` and `audio/theme.wav` (PCM WAV) |
| [GoogleLogoAram](GoogleLogoAram/README.md) | Streamed HTTP to `AramStream` with standard `Stream.CopyToAsync`; partial-read-safe PNG signature verification | Run the PC relay and configure its LAN address; returns 0 on success, otherwise 1 |

## Build a GameCube DOL

Every sample has a solution named after its directory, such as
[Snake/Snake.sln](Snake/Snake.sln) or [Pong/Pong.sln](Pong/Pong.sln).
Install .NET 8, Dolphin, and Docker with Linux containers, or a native
Linux/macOS devkitPPC/libogc2 toolchain; see [IDE setup](../docs/ide-workflow.md).

- **Visual Studio:** open the sample's `.sln`, select its executable as the
  startup project, choose **Dolphin**, and press **Ctrl+F5**.
- **Rider:** open the sample's `.sln`, disable **Use ReSharper Build**, select
  **<sample> in Dolphin**, and click **Run**.

Choose any sample directory name from the table for the CLI:

```sh
sample=ManagedGameCube
dotnet build "samples/$sample/$sample.sln" -c Release
dotnet run --project "samples/$sample" -c Release --no-build
```

Build produces `samples/<sample>/bin/Release/net8.0/<sample>.dol` and the
matching `.elf`. Every real build regenerates and fully rebuilds native code.
Omit `--no-build` to build and launch in one command. Each sample preserves its
own Dolphin settings in `obj/dolphin-user/`. Configure controllers, storage and
networking as required by the table above; building does not provision external
assets or network services.

### Manual bundle generation

For a standalone bundle, opt out of the integrated native build first:

```sh
dotnet build "samples/$sample/$sample.csproj" -c Release -p:DndBuildNative=false
dotnet run --project tools/DolphinDotNet.Compiler -- \
  --bundle "samples/$sample/bin/Release/net8.0/$sample.dll" "out/$sample" \
  --platform gamecube
make -C "out/$sample"
```

See the [standalone build guide](../docs/standalone-native-builds.md) for SDK
dependencies. Load `out/<sample>/Game.dol` in Dolphin or on a GameCube.
`sh scripts/compile-sample.sh` generates HelloGameCube's standalone bundle.

To inspect generated C without the console SDK:

```sh
dotnet run --project tools/DolphinDotNet.Compiler -- \
  --aot "samples/$sample/bin/Release/net8.0/$sample.dll" "out/$sample.c"
```

## Async and platform APIs

The native bootstrap starts the service thread and joins it on exit. Applications
use ordinary awaits; no `Scheduler.Pump` or `NetworkRuntime.Pump` calls are needed.
Default pending awaits capture the current executor. `Task.Run` uses the background
executor and `ConfigureAwait(false)` opts out of capturing the application executor.
Managed execution remains serialized. Keep graphics/input work on the application
executor; see the [async runtime contract](../docs/async-runtime.md).

Platform operations still need explicit initialization: networking samples await
`NetworkRuntime.StartAsync`, and the ARAM sample initializes `Aram`. Resource owners
use `using` and networking stops in `finally`.

These focused examples do not cover every feature. Models, animation, scene
transforms, cameras and ARAM residency use the separate
[model API](../docs/model-assets-runtime.md); the triangle intentionally uses
immediate rendering without an external model asset. ManagedGameCube enables the
[performance overlay](../docs/debug-overlay.md) and displays
`csharp with dolphin dotnet` above the scene.
Consult the [API inventory](../docs/dotnet-standard-status.md) before adding desktop
.NET APIs: TLS, `ValueTask` and `System.Text.Json` are not supported.

## Networking setup and checks

AsyncNetworking defaults match `tests/network_fixtures.py`: DNS on loopback port
15353 and `http://config.test:18081/json`. For hardware, change `ConfigUrl`,
`DnsPort` (usually 53) and `DnsServers` to reachable LAN services. Loopback on a
console does not refer to the development PC. HTTP currently has no TLS support.

CI compiles all samples. It checks HelloGameCube's AOT output,
records ManagedGameCube's complete frame and Start-button exit through a host
backend, and executes AsyncNetworking against the DNS/HTTP fixture with normal
and stressed GC. AudioDemo is compiled through AOT; GoogleLogoAram is compiled through AOT; its live relay and console
network/ARAM operation need hardware testing.
