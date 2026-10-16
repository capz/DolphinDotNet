# Build and run from an IDE

Open **`samples/Pong/Pong.sln`** in Visual Studio 2022 (17.8+) or Rider with
.NET 8 support. Pong is the startup project and needs no external assets.

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
2. Choose the **Dolphin** launch profile in the toolbar.
3. Build normally, then use **Ctrl+F5 / Start Without Debugging**. That action also
   builds before launching. A build failure prevents launch.

## Rider

Open the same solution and select the checked-in **Pong in Dolphin** run
configuration. Click **Run**. Its before-launch Build task produces the DOL and
then launches the Dolphin executable profile. If Rider has not imported the
shared configuration, create a **.NET Launch Settings Profile** configuration
for `Pong.csproj`, framework `net8.0`, profile **Dolphin**, with a Build task.
Disable **Use ReSharper Build** in **Settings → Build, Execution, Deployment →
Toolset and Build** for this solution so Rider invokes MSBuild on every run
instead of skipping an unchanged project.

These configurations launch an emulator process. C# breakpoints, managed
stepping, Hot Reload and a C# debugger attached to emulated PowerPC code are not
implemented. The ELF retains native symbols for Dolphin's separate debugger.

## Command line

From the repository root:

```sh
dotnet build samples/Pong/Pong.sln
dotnet run --project samples/Pong
```

`dotnet run` builds first, including the full native build. To launch the last
successful output without building, use `dotnet run --project samples/Pong --no-build`.
The `CLI` launch profile uses the same launcher as the IDE's `Dolphin` profile.
You can use `-c Release` for either command.

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
- `samples/Pong/obj/Debug/net8.0/native/`: generated C, Makefile and native objects.
- `samples/Pong/obj/dolphin-user/`: persistent, isolated Dolphin user directory.

Every real build regenerates the bundle and removes/rebuilds all native objects,
including the EXI overlay. There is no native incremental build cache. Visual
Studio's fast up-to-date shortcut is disabled. Restore, IntelliSense/design-time
builds and project references to the ordinary runtime libraries do not run the
native pipeline. A failed native build removes previous published native outputs.

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
and select the Dolphin profile; it uses `$(TargetDir)` and does not hard-code Pong's
output name. A Rider shared configuration must point at the new `.csproj`.

For compiler/host tests that only need the managed assembly, explicitly set
`-p:DndBuildNative=false`. Such builds do not create or update a runnable DOL.
Other samples keep their existing manual build workflow until they adopt these
imports; this avoids unexpectedly requiring a console toolchain for all tests.
