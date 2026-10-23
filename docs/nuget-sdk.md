# NuGet SDK and external projects

DolphinDotNet is a closed-world CIL-to-C AOT framework for GameCube, distributed
as `DolphinDotNet.Sdk`, `DolphinDotNet.GameCube`, `DolphinDotNet.Core` and
`DolphinDotNet.Templates`. Install .NET 8, Dolphin, and Docker with Linux containers
(or devkitPPC/libogc2 for the Native backend). Python 3, Bash, Git and Make are
used inside the native toolchain. The SDK carries native **sources**, compiler,
build driver, effects compiler and DAP adapter; it does not redistribute Dolphin,
Docker, or devkitPPC. No repository checkout is required by consumers.

## Start a project

```sh
dotnet new install DolphinDotNet.Templates::0.1.0-preview.1
dotnet new gamecube-game -n MyGame
cd MyGame
dotnet build -c Release
dotnet run -c Release --no-build --launch-profile 'Dolphin Play'
```

`gamecube` is the minimal game entry point; `gamecube-game` provides the same
ready-to-run frame loop. `gamecube-lib` creates a managed library with native
compilation disabled by its `OutputType=Library`. Reference libraries normally
using `ProjectReference` or `PackageReference`. To use another preview, install
that template package version; the generated SDK and GameCube versions match it.
Existing projects can upgrade both references explicitly:

```xml
<Project Sdk="DolphinDotNet.Sdk/0.1.0-preview.1">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup>
    <PackageReference Include="DolphinDotNet.GameCube" Version="0.1.0-preview.1" />
  </ItemGroup>
</Project>
```

For local packages, add an absolute local feed to `NuGet.Config` in the project
parent **before** creating/evaluating the SDK project. Keep nuget.org available
for framework dependencies. The NuGet MSBuild SDK resolver reads these sources.
Alternatively pin `msbuild-sdks.DolphinDotNet.Sdk` in `global.json` and use an
unversioned SDK name. `eng/pack.py --version 0.1.0-preview.2` creates all four
packages in `out/packages`; it never uploads them. It rewrites template versions
in temporary staging, preserving the checkout. Pack with the .NET 8 SDK.

## Build and debug

Both installed IDE extensions operate on external project output directories;
their adapters are bundled with the extensions. Open the generated `.csproj` or
add it to a solution. Select **Dolphin Play** for ordinary launches. Set a local
`DolphinDotNet.local.props` to configure `DolphinPath`, `DndBuildBackend`, or
`DolphinUserDirectory`. This file is machine-specific and should be ignored by
source control. Docker supports project and NuGet cache paths containing spaces.
Native Make requires paths without whitespace. See [IDE setup](ide-workflow.md)
for backend requirements and proxy/CA forwarding.

For debugging, build Debug, select **Dolphin Debug**, and launch without the CLR
debugger. Dolphin waits on `127.0.0.1:2166`. In Visual Studio install the
[DolphinDotNet extension](visual-studio-debugging.md) and use **Debug → Attach to
DolphinDotNet…**. In Rider install the [plugin](rider-debugging.md), disable
ReSharper Build, and use **Run → Attach to DolphinDotNet…**. Select the external
`bin/Debug/net8.0` directory containing the matching ELF, `.dnd-debug.json` and
`.dnd-addresses.json`. Choose the TCP target at port 2166. Bindings and portable
PDBs must come from that build. Relocated source trees use the extensions' source
path mappings. Hot Reload and CLR attach are unsupported. Interactive desktop IDE
operation requires testing on its host OS; CLI/package checks do not establish it.

The SDK's `tools/debugger/DolphinDotNet.DebugAdapter.dll` is also available to
DAP clients using `dotnet <adapter-path>` over stdio. It is an optional direct
adapter entry point, not an additional required global tool installation.
`dotnet <sdk>/tools/build/DolphinDotNet.Build.dll doctor --build-only true`
checks the selected native backend. Run `--help` for build/run arguments.

## Dependency and API contract

NuGet resolves target frameworks and versions normally. The SDK copies runtime
implementation assemblies into output, including transitive libraries. AOT walks
their assembly references and compiles reachable managed methods into one native
program; it does not start a CLR or load DLLs on the console. Host framework
assemblies are handled by supported Dolphin facades/intrinsics. A compatible
NuGet target framework alone does **not** establish GameCube compatibility.

Use managed libraries whose reachable code fits the documented
[supported API surface](system-api.md). Reflection, dynamic assembly loading,
JIT/Reflection.Emit, arbitrary P/Invoke, and native NuGet runtime assets require
an explicit console implementation; desktop native libraries cannot be linked.
Unsupported reachable calls fail with method identity, caller and IL offset.
Unused unsupported method bodies are not AOT compiled. Assembly references must
still resolve for metadata/type layout. Duplicate fully qualified type names
across dependency assemblies are rejected because the current model uses a
single type namespace. Framework-name assembly replacements are unsupported.

Diagnostics include `DND1001` (missing copied implementation; restore and check
`CopyLocalLockFileAssemblies`), `DND1002` (duplicate type; rename/remove conflict),
and `DND1003` (unsupported/missing/ambiguous API; replace it with a supported API
or supply a compiler intrinsic). MSBuild wraps compiler diagnostics as `DND002`.
A failed native build deletes previous runnable outputs to prevent stale launch.
Design-time evaluation and library builds do not invoke Docker.

## Validation

```sh
python3 eng/pack.py
python3 tests/sdk_consumer.py --native --workspace '/tmp/external consumer'
```

The consumer test relocates the feed, creates an empty package cache, installs
all three templates, builds them, exercises real transitive NuGet packages and
missing/unsupported API diagnostics, and optionally builds external Pong with
Debug mappings. The existing Dolphin smoke harness can test the resulting
`ExternalPong.dol` and ELF; require boot and `main`, `dnd_aot_entry`,
`dnd_audio_create_pcm8`, and `dnd_device_end` probes. See [release process](releases.md)
for test and publication requirements. The [standalone examples](https://github.com/capz/DolphinDotNet.Samples) now reference the published preview SDK and runtime packages, instead of local source projects. CI checks out that repository when integration tests require sample code.

## Optional content tools

`python3 eng/pack.py --include-tools` additionally packages the existing
`DolphinDotNet.Assets` (`dnd-assets`) and `DolphinDotNet.SaveImage`
(`dnd-save-image`) .NET tools using the same version. Install them in a local
manifest (`dotnet new tool-manifest`, then `dotnet tool install <package>
--version <version>`) for reproducibility. Use `--add-source <feed>` for local
packages. They are optional and do not participate in the GameCube runtime
assembly graph. See their tool READMEs for conversion commands/dependencies.
Dependency packages without PDBs compile in Debug but have no source/locals
mapping; supplied symbols are validated and entry-project symbols are required.
