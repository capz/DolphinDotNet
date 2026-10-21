# Visual Studio debugging

The DolphinDotNet VSIX adds **Debug → Attach to DolphinDotNet…** to Visual Studio
2022 17.8–17.x, x64. It uses Microsoft's built-in Debug Adapter Host and the same
.NET DAP adapter as Rider. The packaged adapter includes its own Windows .NET
runtime. Installing the VSIX requires restarting Visual Studio.

Download **DolphinDotNet-VisualStudio-plugin** from the **Visual Studio VSIX**
workflow (or the VSIX asset from a full main-build release), extract the `.vsix`, close Visual Studio and install it with VSIXInstaller.
This is a first integration: automated checks cover extension compilation,
packaging, configuration and real adapter protocol behavior. Interactive Visual
Studio UI validation and physical-console compatibility remain required before
claiming end-to-end parity. Visual Studio 2026 is not included in the manifest yet.

1. Open a saved sample solution such as `samples/Snake/Snake.sln` and build Debug.
2. Start its **Dolphin Debug** launch profile with **Ctrl+F5** so the emulator's
   GDB stub waits on `127.0.0.1:2166`. The extension attaches to an already-running
   debug DOL; it does not build, upload or start the target.
3. Select **Debug → Attach to DolphinDotNet…**. Choose the sample's
   `bin/Debug/net8.0` folder. It needs the matching `Snake.elf`,
   `Snake.dnd-debug.json` (or `dnd-il-debug.json`) and `Snake.dnd-addresses.json`.
4. Choose **BBA / TCP**, host `127.0.0.1`, port `2166`, then **Attach**.
   A physical TCP target uses its actual IP/port. USB Gecko uses its Windows COM
   port and driver baud rate; its EXI slot must match the debug build.
5. Set an ordinary C# line breakpoint, for example on Snake's `Controller.Update();`
   line, then **Continue**. Native startup may initially show a partial stack.
6. Use **Step Into / Step Over / Step Out**, **Break All**, **Call Stack**, and
   **Debug → Windows → Locals**. Arguments appear as a scope alongside locals.

Build folders, target settings and source mappings are remembered in
`<solution folder>/.vs/DolphinDotNet/attach.json`. Source mappings are a JSON object
from build-machine path prefixes to local prefixes, for example:

```json
{ "/home/runner/work/DolphinDotNet/DolphinDotNet": "D:/Projects/DolphinDotNet" }
```

The optional live-status fields take this computer's IPv4 address reachable from
the game and a UDP port (`0` chooses automatically). They reuse the adapter's
existing heartbeat; normal BBA GDB and unsupported builds retain their documented
live-status limits.

Inspection is read-only and only available in supported stopped managed frames:
primitive arguments/locals, supported non-generic reference objects and bounded
string previews. Larger scopes/objects use variable pages. Arrays, struct
expansion, statics, getters, expression evaluation, Watch expressions, editing,
Hot Reload and managed exception breakpoints are unsupported. Stepping/unwind
limits are shared with Rider. Conditional breakpoints and logpoints are rejected.
Breakpoint edits made while running are queued until the next stop; **Break All**
applies them. The adapter reports their verification through breakpoint events.

Disconnect removes owned breakpoints and closes the connection, normally leaving
the target stopped. It does not kill Dolphin or the game. If connection fails,
check the actual target listener, matching artifacts and source mappings.

For host diagnostics, open **View → Other Windows → Command Window**, then run:

```text
DebugAdapterHost.Logging /On /OutputWindow
```

Logs appear in the **Debug Adapter Host Log** Output pane. Our attach command is
independent of the ordinary managed Attach to Process engine and the project's
default .NET debugger launch.

## Build and acceptance

On Windows with Visual Studio's extension development workload and .NET 8 SDK:

```powershell
msbuild plugins/visual-studio/DolphinDotNet.VisualStudio.csproj /restore /p:Configuration=Release
dotnet run --project tests/VisualStudioTests -c Release
```

The VSIX appears under `plugins/visual-studio/bin/Release/net472/`. The separate
Windows workflow can be run independently with Actions → Visual Studio VSIX → Run workflow,
and the main CI calls it when selected. It verifies its manifest, engine registration and self-contained
adapter payload. On non-Windows hosts, `-p:CompileOnly=true` compiles extension
code against the VS SDK without invoking Windows packaging tools.

Before release, install into a Visual Studio experimental instance and exercise
Snake attach, an existing C# gutter breakpoint, all three steps, pause, frame
selection, primitive/object/string Locals expansion, variable pages, breakpoint
edits during execution, failed attach, target loss and detach while running.
Confirm these actions leave no orphan adapter and never terminate the target.

Host integration follows Microsoft's [Debug Adapter Host samples](https://github.com/microsoft/VSDebugAdapterHost),
[packaging walkthrough](https://github.com/microsoft/VSDebugAdapterHost/wiki/Packaging-a-VS-Code-Debug-Adapter-For-Use-in-VS)
and [special host properties](https://github.com/microsoft/VSDebugAdapterHost/wiki/Special-Properties-Recognized-by-the-Debug-Adapter-Host).
