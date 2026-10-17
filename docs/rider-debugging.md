# Rider attach debugging (A6 / primitive A7 inspection)

`plugins/rider` implements the approved native Rider attach configuration and target
editor. It uses public XDebugger APIs and Eclipse LSP4J DAP over stdio to the bundled
.NET 8 adapter. It does not require LSP4IJ. The SDK is pinned to **Rider 2026.2.3.1
(build 262)**, Java 25, Gradle 9.1.0 and IntelliJ Platform Gradle Plugin 2.19.0;
LSP4J debug is pinned to 0.24.0. Compatibility is currently limited to 262.*.

**Baseline:** PR #77 passed the complete Gradle package, host protocol/PTY and
real-Dolphin checks in [run 38944870062](https://github.com/capz/DolphinDotNet/actions/runs/38944870062).
This follow-up adds lifecycle fault handling, Plugin Verifier and primitive inspection;
its CI results must be checked separately.
Interactive Rider C# gutter coexistence, Debug window behavior and physical console
attach remain separate acceptance gates. Do not describe SDK compilation, fake
peers or emulator CI as interactive Rider or USB Gecko/BBA hardware validation.

## Build, install and attach

Requires JDK 25, a .NET 8 SDK to build the adapter, and Python 3 for tests:

```sh
cd plugins/rider
./gradlew test buildPlugin verifyPlugin
# Optional: reuse an extracted matching Rider SDK instead of downloading it:
./gradlew -PriderSdk=/absolute/path/to/Rider test buildPlugin
```

The 0.2 development plugin uses ID `com.dolphindotnet.debugger` for Plugin
Verifier compatibility. Remove an older 0.1 development installation before
installing this ZIP, since its earlier ID was `com.dolphindotnet.rider`.

Install the ZIP from `plugins/rider/build/distributions` via Rider **Settings →
Plugins → Install Plugin from Disk** and restart. The ZIP includes the adapter and
its managed/native serial dependencies. The Rider process needs `dotnet` (.NET 8
runtime) on PATH. Use a .NET SDK when rebuilding the adapter, not just its runtime.
The plugin starts the adapter directly; its stdout is reserved for DAP, and stderr
is shown in the Debug console. It never starts or kills Dolphin/the console.

Debugger target artifacts must come from **managed Debug builds** (`-c Debug`), with
Portable PDBs and native debug symbols. Release-mode debugging is out of scope;
Release optimization is unchanged. The adapter/plugin may themselves be packaged
in Release: that does not change the target configuration.

SDK Debug builds now enable `DndDebugSymbols` by default. For example, from the
repository root run `dotnet build samples/Snake/Snake.csproj -c Debug`, then choose
`samples/Snake/bin/Debug/net8.0` in the attach configuration. A successful build
publishes `Snake.elf`, `Snake.dnd-debug.json` and `Snake.dnd-addresses.json` together.
If maps are missing, check that the native build succeeded and no local setting
disabled `DndDebugSymbols`; `-p:DndDebugSymbols=true` overrides that setting.
See [IDE build troubleshooting](ide-workflow.md) for Windows Docker lookup,
interrupted native cleanup and NuGet restore errors.

1. Start the matching debug DOL in Dolphin or on the console with its GDB stub
   waiting/stopped. Build, upload and emulator launch remain external steps.
2. Add a **DolphinDotNet Attach** run/debug configuration. Choose the debug build
   folder containing exactly one `*.elf`, its `<name>.dnd-addresses.json`, and either
   the published `<name>.dnd-debug.json` or native-bundle `dnd-il-debug.json`.
   The three artifact rows display the resolved filenames; the adapter validates
   their matching SHA-256 identities before connecting.
3. Open **Manage targets…**, add a named connection and save it. Choose that saved
   target in the configuration dropdown. Missing/deleted target IDs produce an
   error; the plugin never silently switches to another console.
4. Expand **Source path mappings** for CI/container builds. Add build-source prefix
   → local-source prefix entries. `$PROJECT_DIR$` is supported in the debug build
   folder and local mapping paths; mappings remain case-sensitive and bidirectional.
5. Select the DolphinDotNet configuration **before** placing C# gutter breakpoints.
   This enables its dedicated breakpoint type. Existing Rider .NET breakpoints
   belong to the normal debugger and are not imported or rewritten. C# breakpoint
   selection/coexistence must still be checked interactively on the pinned Rider.
6. Use **Debug**. Attach performs initialize → attach → initialized → source
   breakpoints → configurationDone. It initially leaves execution stopped. Use the
   standard Rider Resume, Pause, Step Into, Step Over and Step Out controls and
   stack frame source navigation. Stop disconnects the adapter without a target kill.

## Saved target properties

Profiles live in the IDE's machine-local `dolphindotnet-targets.xml` with roaming
**disabled**. Run configurations store a stable target ID; debug build/source paths
remain per run configuration. Sharing a run configuration does not share serial
paths or target endpoints: define/select a target on the receiving machine.
The target dialog edits copies and commits changes only on Save; Cancel leaves the
stored profiles intact.

| Setting | BBA / TCP (also Dolphin) | USB Gecko |
|---|---|---|
| Name | Required | Required |
| Host / TCP port | Required; match emulator/stub | Not sent |
| Serial device | Not used | Required editable picker, with Refresh; COM5, /dev/ttyUSB0, /dev/cu.usbserial-* are examples |
| Expected console slot | Not used | A/B build expectation only; B by default |
| Response timeout | Advanced; 1–120 seconds, default 10 | Same |
| Driver baud compatibility | Not used | Advanced; positive integer, provisional 115200 default |

USB opens the FTDI VCP using **8 data bits, no parity, 1 stop bit, no handshake**,
matching the existing RSP backend. FT245 is a USB FIFO, so its driver baud setting
is not an assertion of physical UART transfer speed. The compatibility default is
not a tested hardware recommendation. Refresh runs the packaged adapter's
`--list-serial-ports` command, lists OS serial ports without claiming they are USB
Geckos, and preserves manually entered paths. Driver installation, OS permissions
and exclusive port access remain host prerequisites.

The expected EXI slot does not go into DAP attach arguments. The DOL was built with
`DndGdbTransport=UsbGecko` and `DndGdbChannel=0` (slot A) or `1` (slot B); only a rebuild
can change that. BBA subnet/gateway/initialization also belong to the target build,
not the host attach profile. No target networking configuration is sent on attach.

## Adapter contract

The `.NET 8` adapter in `tools/DolphinDotNet.DebugAdapter` can also be used directly:

```sh
dotnet build tools/DolphinDotNet.DebugAdapter -c Release
dotnet tools/DolphinDotNet.DebugAdapter/bin/Release/net8.0/DolphinDotNet.DebugAdapter.dll
# Enumeration is a separate process mode that prints one JSON array, not DAP:
dotnet tools/DolphinDotNet.DebugAdapter/bin/Release/net8.0/DolphinDotNet.DebugAdapter.dll --list-serial-ports
```

Example TCP attach arguments (not an entire IDE configuration):

```json
{
  "transport": "tcp",
  "host": "127.0.0.1",
  "port": 2166,
  "timeoutSeconds": 10,
  "elf": "/artifacts/debug/Game.elf",
  "sourceMap": "/artifacts/debug/dnd-il-debug.json",
  "addressMap": "/artifacts/debug/Game.dnd-addresses.json",
  "sourceFileMap": {"/__w/DolphinDotNet/DolphinDotNet": "/local/DolphinDotNet"}
}
```

For USB replace transport/host/port with `"transport": "usbGecko"`,
`"serialPort": "COM5"`, `"baudRate": 115200`. Baud is explicit at the DAP boundary.
Omitting transport retains legacy TCP behavior. Unknown transports, missing serial
paths, nonpositive baud, invalid TCP ports and invalid timeouts fail before connecting.

The adapter supports one all-stop PowerPC context, source breakpoint replacement,
stack pagination, continue/pause, bounded source Step Into/Over/Out and disconnect.
Rider keeps desired breakpoint changes while running and applies them at the next
stop (or before resume); direct DAP clients must pause before changing breakpoints.
Conditional breakpoints/logpoints remain explicitly unsupported. Native frames keep
PCs without invented source positions; partial unwind/step-limit reasons go to the
console. `scopes`/`variables` expose read-only primitive Arguments and Locals in
Rider’s existing Variables pane. Stack and variable handles expire on resume.
Evaluation and mutation are disabled.

Disconnect interrupts a running target, waits for its stop reply, removes owned
breakpoints and closes the connection. It sends no kill command and normally leaves
the target stopped. Failed cleanup closes the adapter session and reports an error;
reliable physical reconnect is not yet claimed. The Rider bridge waits for graceful
adapter exit, then forcibly closes only the adapter if it is unresponsive.

## Primitive inspection contract

The compiler joins Portable PDB parameter/local names and lexical IL scopes to
generated native slots. The address resolver accepts verified CFA-relative
locations and PowerPC register locations, including bounded DWARF location lists.
Values are decoded as big-endian Boolean, Char, signed/unsigned integers, native
integers, Single or Double. Caller frames permit proven CFA-relative memory only;
register-backed values are restricted to the top frame. Missing, ambiguous or
unsupported locations display an explicit unavailable reason. No expression is
evaluated to recover a value. Objects, strings, arrays, fields and properties are
not expanded. Scope visibility is restricted to proven mapped statement addresses.

## Acceptance evidence and remaining checks

Automated host checks:

- `tests/test_debug_adapter.py`: real adapter subprocess with fake RSP TCP target;
  protocol framing, source identity/maps, breakpoint ownership, stack/stepping,
  pause/resume/disconnect, primitive values, pagination, expired handles, invalid
  transports and serial enumeration.
- Its Linux PTY test exercises actual `System.IO.Ports` open/read/write, source
  breakpoint, stack, pause and cleanup against a byte relay to a fake RSP peer.
  This validates host serial plumbing, **not** FTDI USB enumeration, EXI or hardware.
- `plugins/rider/src/test`: TCP/USB argument separation, slot exclusion, input
  validation, stable IDs/deep-copy state, deletion without fallback, native and
  MSBuild artifact discovery, and the LSP4J subprocess initialize/configure/stack/
  resume/pause/Step Into/Over/Out/disconnect sequence. Fault tests cover adapter exit,
  lost stdout, rejected attach, timeout and cancellation. The Java client also runs
  against the actual .NET adapter over fake TCP and Linux PTY RSP targets.
- A packaged-JAR test verifies adapter discovery from custom install paths with
  spaces/Unicode and the error for a missing adapter payload.
- CI builds the pinned Rider plugin, runs tests and Plugin Verifier, and uploads the
  installable ZIP plus failure reports.
  Source-only plugin changes select that job; debugger changes also build its consumer.

Real-Dolphin DAP acceptance passed run
[38934685398](https://github.com/capz/DolphinDotNet/actions/runs/38934685398) on
`aa63a0499e25fe0c98ba31b6205d792218ee3b5f`: two managed breakpoints, source stepping,
pause, Inner → Outer → Main stack, Step Out to Outer:14 and Step Over to Outer:15.
The new increment requires its own CI result; that historical run proves emulator/DAP
behavior only.

Before claiming A6 validated, record the exact installed Rider/plugin versions,
configuration save/reload and Cancel behavior, BBA/USB transport field switching,
serial refresh/error handling, dropdown selection, C# gutter coexistence, source
navigation, pause/resume, stack selection and all three step controls against the
real Dolphin fixture. Verify Stop leaves the emulator alive and no owned breakpoint
installed. Keep A4.4 physical BBA and USB Gecko validation deferred separately.

Out of scope: DAP launch/build/upload orchestration, automatic target discovery or
slot negotiation, verified hardware compatibility, reconnect, object inspection,
expressions, managed exception filters, async stacks, real thread enumeration,
reverse execution, Release target debugging, Visual Studio integration and Marketplace
publishing. Recursive Step Into and same-line Step Over use CFA transitions;
unsupported unwind states and instruction budgets still produce diagnostic stops.

## Route decision

The earlier LSP4IJ spike inspected upstream `cbe50d3d9ff7eac9d0055ad7695bf0b1fc5fb4f0`
(0.21.1-SNAPSHOT). Its DAP support matrix marks Pause unsupported, and its
`DAPDebugProcess` has no pause override. Therefore this implementation owns a thin
XDebugger bridge instead of depending on or forking LSP4IJ. See the public
[XDebugger APIs](https://github.com/JetBrains/intellij-community/tree/master/platform/xdebugger-api)
and [LSP4J DAP client launcher](https://github.com/eclipse-lsp4j/lsp4j/blob/v0.24.0/org.eclipse.lsp4j.debug/src/main/java/org/eclipse/lsp4j/debug/launch/DSPLauncher.java).
