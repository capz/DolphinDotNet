# Build and run from an IDE

Open **`samples/Pong/Pong.sln`** in Visual Studio 2022 (17.8+) or Rider with
.NET 8 support. Pong is the startup project and needs no external assets.
Every other sample has the same layout: open `samples/<sample>/<sample>.sln`
and select **<sample> in Dolphin (play without debugger)** in Rider or the **Dolphin Play** profile in Visual
Studio. See the [sample catalog](../samples/README.md) for runtime requirements.

Install the **.NET 8 SDK**, **Dolphin**, and one build backend:

| Backend | Setup |
| --- | --- |
| Docker (Windows, Linux, macOS) | Install Docker Desktop/Engine, start it, and select Linux containers. The first build downloads the same toolchain image as CI, pinned by digest. |
| Native (Linux/macOS) | Install devkitPPC and libogc2, including fat/dvm/iso9660/bba/aesnd libraries. Set `DEVKITPRO` and `DEVKITPPC`. Install `make`, Bash, Git and Python 3; the SDK must include `elf2dol`. Restart the IDE after changing environment variables. |

The container backend uses `linux/amd64`; on Apple Silicon, enable Docker
Desktop's x86 emulation if required. The native backend does not use Docker.

`DndBuildBackend=Auto` chooses Native on Linux/macOS when `DEVKITPRO` is set,
otherwise Docker. Windows uses Docker; native MSYS/WSL builds are not wired up.
The Docker build mounts only the generated native directory as `/work`, so spaces
in Windows/macOS checkout paths work. Native Make builds require checkout and SDK
paths without whitespace; select Docker when paths contain spaces.

The EXI correction script fetches pinned upstream source during every native
rebuild. Git/network access is required. Docker needs access to GHCR for the
initial image download and GitHub from inside the container during builds.
Existing HTTP(S) proxy environment variables are forwarded by name to Docker.
If `GIT_SSL_CAINFO`, `SSL_CERT_FILE` or `CURL_CA_BUNDLE` points to a certificate
bundle, it is mounted read-only and used for Git/TLS verification inside the
container. Host-specific proxy hostnames must also resolve inside Docker.

## Visual Studio

1. Open `samples/Pong/Pong.sln`, and select **Pong** as the startup project if needed.
2. Choose the **Dolphin Play** launch profile in the toolbar.
3. Build normally, then use **Ctrl+F5 / Start Without Debugging**. That action also
   builds before launching. A build failure prevents launch.

For C# source debugging, install the [Visual Studio extension](visual-studio-debugging.md)
and use **Debug → Attach to DolphinDotNet…** after launching a matching Debug DOL
with its GDB stub waiting.

## Rider

Open the same solution and select the checked-in **Pong in Dolphin (play without debugger)** run
configuration. Click **Run**. Its before-launch Build task produces the DOL and
then launches the Dolphin executable profile. If Rider has not imported the
shared configuration, create a **.NET Launch Settings Profile** configuration
for `Pong.csproj`, framework `net8.0`, profile **Dolphin Play**, with a Build task.
Disable **Use ReSharper Build** in **Settings → Build, Execution, Deployment →
Toolset and Build** for this solution so Rider invokes MSBuild on every run
instead of skipping an unchanged project.

The existing **<sample> in Dolphin** configuration now uses **Dolphin Debug** and
waits for Rider on `127.0.0.1:2166`. This corrects older configurations that kept
launching without GDB after a separate debugger profile was added. After pulling
an update, reload the solution so Rider picks up the shared configuration change.
Debug **CLI** / project launches also enable the same GDB port through the shared
SDK's `RunArguments`; Release CLI launches continue without a debugger.
The original **Dolphin** launch-settings profile is also a debugger launch, so
existing configurations that refer to it pick up GDB after a reload. Choose
**Dolphin Play** explicitly when creating a launch-settings profile for normal play.

These configurations launch an emulator process. For C# source debugging, install
the [DolphinDotNet plugin](rider-debugging.md) and choose **Run → Attach to
DolphinDotNet…** after starting Dolphin with its GDB stub waiting/stopped. The
dialog remembers the debug build folder, saved target and source mappings.
C# gutter breakpoints recognize DolphinDotNet SDK projects automatically.
Existing attach configurations remain usable for multilaunch. Hot Reload is not supported.

For Snake, open `samples/Snake/Snake.sln`, select **Debug**, and run **Snake in
Dolphin (wait for Rider)**. This creates Dolphin's debugger settings automatically
in a separate profile and prints their exact path; you do not need to locate or
create `Dolphin.ini`. Dolphin waits for Rider on `127.0.0.1:2166`. In
**Run → Attach to DolphinDotNet…**, select
`$PROJECT_DIR$/bin/Debug/net8.0` as the debug build folder (when Snake's solution
is open). It must contain `Snake.elf`, `Snake.dnd-debug.json` and
`Snake.dnd-addresses.json` from the same successful build. Under **Manage targets…**,
save and select a **BBA / TCP** target with host `127.0.0.1` and port `2166`.
Leave that Dolphin launch running, then click **Attach** in the dialog. The DOL/ELF are GameCube artifacts, not Windows executables.

## Command line

From the repository root:

```sh
dotnet build samples/Pong/Pong.sln
dotnet run --project samples/Pong -c Release
```

`dotnet run` builds first, including the full native build. To launch the last
successful Release output without building, use `dotnet run --project samples/Pong -c Release --no-build`.
The `CLI` launch profile uses the same build/run driver as the IDE profiles.
You can use `-c Release` for either command.
Debug CLI launches with symbols enabled wait for Rider at `127.0.0.1:2166`.
For ordinary play, use `-c Release` or the IDE's **play without debugger** profile.

From the repository root, the equivalent Snake build is:

```powershell
dotnet build samples/Snake/Snake.csproj -c Debug
```

`dotnet build` restores NuGet packages automatically. If the IDE reports
`NETSDK1004` after deleting generated files, run
`dotnet restore samples/Snake/Snake.csproj`, then build again. Do not use
`--no-restore` until restore has succeeded. Running `dotnet build Snake.csproj`
from the repository root fails because that project lives under `samples/Snake`.

Check prerequisites separately after the driver has been built:

```sh
dotnet run --project tools/DolphinDotNet.Build -- doctor
dotnet run --project tools/DolphinDotNet.Build -- doctor --backend Native --build-only true
```

## Local settings

Dolphin is discovered on `PATH`, under `/usr/games`, in
`/Applications/Dolphin.app`, or in the standard Windows Program Files location.
For a portable installation, set **`DOLPHIN_PATH`** to the executable (on macOS,
the executable inside the application bundle).

Alternatively, create **`samples/Pong/DolphinDotNet.local.props`**. It is ignored
by Git, and works for both IDEs and the CLI:

```xml
<Project>
  <PropertyGroup>
    <DndBuildBackend>Docker</DndBuildBackend>
    <DolphinPath>C:\Tools\Dolphin\Dolphin.exe</DolphinPath>
    <DndBuildJobs>4</DndBuildJobs>
  </PropertyGroup>
</Project>
```

`DolphinPath` takes precedence over `DOLPHIN_PATH`; neither is required if
discovery succeeds. Missing Dolphin does not prevent compiling a DOL. Its path
is validated when you run. Setup/driver errors use `DND001`; AOT compiler errors
use `DND002` in the IDE's error list. Native compiler diagnostics retain their
generated file/line locations; Docker's `/work` paths are mapped back to the
host's generated directory.

## Outputs, rebuilds and emulator settings

For a Debug build:

- `samples/Pong/bin/Debug/net8.0/Pong.dol`: runnable game.
- `samples/Pong/bin/Debug/net8.0/Pong.elf`: native symbols.
- `samples/Pong/bin/Debug/net8.0/Pong.dnd-debug.json`: managed source/inspection map.
- `samples/Pong/bin/Debug/net8.0/Pong.dnd-addresses.json`: ELF address/variable map.
- `samples/Pong/obj/Debug/net8.0/native/`: generated C, Makefile and native objects.
- `samples/Pong/obj/dolphin-user/`: persistent, isolated Dolphin user directory.

Every real build regenerates the bundle and removes/rebuilds all native objects,
including the EXI overlay. There is no native incremental build cache. Visual
Studio's fast up-to-date shortcut is disabled. Restore, IntelliSense/design-time
builds and project references to the ordinary runtime libraries do not run the
native pipeline. A failed native build removes previous published native outputs.

Debug builds enable `DndDebugSymbols` by default and preserve Portable PDB locals.
Release builds keep their existing optimization and symbol defaults. Set
`-p:DndDebugSymbols=false` to opt out explicitly. Rebuild before attaching; copying
maps from another build fails the adapter's artifact identity checks.

The build driver clears read-only generated Git files and keeps its ownership
marker during cleanup, allowing a failed cleanup to be retried. It still refuses
to delete an unowned native directory. If an older driver left an unowned partial
directory, close any processes using it and remove only
`samples/<sample>/obj/Debug/net8.0/native`, then rebuild. Keep the rest of `obj`
to preserve restored NuGet assets. A genuinely locked file or permissions failure
still needs to be resolved before retrying.

On Windows, the driver resolves `docker.exe` rather than Docker Desktop's
extensionless Unix shell script. A "not a valid application for this OS platform"
error naming `resources/bin/docker` indicates an older driver; update and rebuild.
Docker Desktop must be running Linux containers.

If an older build reports `No verifiable native breakpoint addresses` on Windows,
update and rebuild. The address resolver now requests full DWARF filenames from
`readelf --wide`; otherwise long Windows PDB paths can be truncated by the Unix
toolchain and fail source matching. Native warnings about misleading indentation
or unused `roots` do not cause this mapping failure.

If Bash reports `set: pipefail` / `invalid option name` in
`scripts/build_exi_overlay.sh`, the shell script has Windows CRLF line endings.
Bundle generation now normalizes the copied script to LF, and `.gitattributes`
keeps shell scripts at LF in new checkouts. Update the sources and rebuild the
sample; no Docker image change is required. The CLI launch profile still builds
through this same native pipeline before launching Dolphin.

`dotnet clean samples/Pong` removes generated native build/output files but keeps
the Dolphin user directory so controller and emulator settings survive. Set
`DolphinUserDirectory` to use another directory. This does not modify your normal
Dolphin user profile. Delete the isolated directory manually to reset it.

On first launch the profile enables two standard controllers. In Dolphin's
**Controllers** dialog, bind both ports to keyboards or gamepads. Pong pauses
when a controller is disconnected. Your bindings are preserved on later runs.
Closing Dolphin returns control to the launcher; Ctrl+C stops the emulator
process tree started by the CLI launcher. IDE Stop behavior also depends on the
IDE's process-tree termination support.

## Reuse in another project

This first SDK is checked into the repository; it is not a published NuGet SDK.
Pong uses explicit SDK imports so no extension or custom SDK resolver is needed:

```xml
<Project>
  <Import Project="../../sdk/DolphinDotNet.Sdk/Sdk/Sdk.props" />
  <ItemGroup>
    <ProjectReference Include="../../src/DolphinDotNet.GameCube/DolphinDotNet.GameCube.csproj" />
  </ItemGroup>
  <Import Project="../../sdk/DolphinDotNet.Sdk/Sdk/Sdk.targets" />
</Project>
```

Adjust the relative paths for the new project. Copy Pong's `Properties/launchSettings.json`
and select Dolphin Debug for attach preparation or Dolphin Play for normal play;
the profiles use `$(TargetDir)` and do not hard-code Pong's
output name. A Rider shared configuration must point at the new `.csproj`.

For compiler/host tests that only need the managed assembly, explicitly set
`-p:DndBuildNative=false`. Such builds do not create or update a runnable DOL.
All samples use these imports. Compiler/host CI paths explicitly disable native
building; the IDE workflow jobs build every sample solution and native DOL.

For projects outside this checkout, use the [NuGet SDK and templates](nuget-sdk.md); generated projects include Dolphin Play/Debug launch profiles.
