# Open and build from the repository root

Open `DolphinDotNet.sln` in Visual Studio 2022 (17.8+) or Rider with .NET 8 support. It groups the runtime, tools, samples and managed tests into solution folders. The Windows Visual Studio extension is built separately using its existing workflow; it requires VSSDK targets and is not included in this cross-platform solution.

For the Hypercube rendering PoC, open `TextureStrategy.slnf` instead. This solution filter loads TextureStrategy and its two runtime dependencies. Select TextureStrategy as the startup project. The SDK builds its compiler and build driver when generating the native output.

Install the .NET 8 SDK and either Docker running Linux containers or the native devkitPPC/libogc2 toolchain. See [IDE setup](docs/ide-workflow.md) for prerequisites, backend selection and Dolphin discovery.

Build the PoC from the repository root:

```sh
dotnet build TextureStrategy.slnf -c Release -m:1
```

Open `samples/TextureStrategy/bin/Release/net8.0/TextureStrategy.dol` in Dolphin. A switches between pages and atlases; B exits. See the [experiment instructions](samples/TextureStrategy/README.md) for what the comparison measures.

Build the whole solution:

```sh
dotnet build DolphinDotNet.sln -c Release -m:1
```

Use one MSBuild node (`-m:1`) for native solution builds: sample build targets rebuild shared compiler/driver outputs. In the IDE, build the selected sample project, or configure solution builds to use one project at a time. Building every sample also builds samples with their own asset/runtime requirements; see the [sample catalog](samples/README.md).

For managed compilation only:

```sh
dotnet build DolphinDotNet.sln -c Release -p:DndBuildNative=false -m:1
```

Managed-only builds do not produce runnable GameCube DOL files. Building the solution compiles test projects; follow their existing instructions to execute tests.

Existing per-sample solutions continue to work. Add new projects to the root solution with `dotnet sln DolphinDotNet.sln add path/to/Project.csproj`.
