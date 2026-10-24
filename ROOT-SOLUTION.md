# Open and build from the repository root

Open `DolphinDotNet.sln` in Visual Studio 2022 (17.8+) or Rider with .NET 8 support. It groups the runtime, tools, TextureStrategy PoC and framework-owned managed tests into solution folders. The other samples and debugger projects live in the independent [Samples](https://github.com/capz/DolphinDotNet.Samples) and [Debuggers](https://github.com/capz/DolphinDotNet.Debuggers) repositories and are not part of this solution.

For the Hypercube rendering PoC, open `TextureStrategy.slnf` instead. This solution filter selects TextureStrategy and its two runtime dependencies. The SDK also references the compiler and build driver as build-only dependencies, so they finish before native compilation. Select TextureStrategy as the startup project.

In Rider, use **File → Open → Open…** and select the `.sln` or `.slnf` file. Opening the repository folder or viewing these files in an editor tab does not load the solution. If Build reports **No loaded projects** with an empty `/t:`, open the solution file through this dialog and let project loading finish before building.

Install the .NET 8 SDK and either Docker running Linux containers or the native devkitPPC/libogc2 toolchain. See [IDE setup](docs/ide-workflow.md) for prerequisites, backend selection and Dolphin discovery.

Build the PoC from the repository root:

```sh
dotnet build TextureStrategy.slnf -c Release
```

Open `samples/TextureStrategy/bin/Release/net8.0/TextureStrategy.dol` in Dolphin. A switches between pages and atlases; B exits. See the [experiment instructions](samples/TextureStrategy/README.md) for what the comparison measures.

Build the whole solution:

```sh
dotnet build DolphinDotNet.sln -c Release
```

Parallel solution builds are supported. Shared compiler/driver outputs are built through project references instead of being rebuilt inside the native build target.

For managed compilation only:

```sh
dotnet build DolphinDotNet.sln -c Release -p:DndBuildNative=false
```

Managed-only builds do not produce runnable GameCube DOL files. Building the solution compiles test projects; follow their existing instructions to execute tests.

The other samples' solutions are maintained in the Samples repository. Add new projects that belong to this repository with `dotnet sln DolphinDotNet.sln add path/to/Project.csproj`.
