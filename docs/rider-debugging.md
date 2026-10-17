# Rider attach debugging (A6 / bounded A7 inspection)

`plugins/rider` implements the approved native Rider attach configuration and target
editor. It uses public XDebugger APIs and Eclipse LSP4J DAP over stdio to the bundled
.NET 8 adapter. It does not require LSP4IJ. The SDK is pinned to **Rider 2026.2.3.1
(build 262)**, Java 25, Gradle 9.1.0 and IntelliJ Platform Gradle Plugin 2.19.0;
LSP4J debug is pinned to 0.24.0. Compatibility is currently limited to 262.*.

**Baseline:** PR #77 passed the complete Gradle package, host protocol/PTY and
real-Dolphin checks in [run 38944870062](https://github.com/capz/DolphinDotNet/actions/runs/38944870062).
The stepping/lifecycle/primitive increment passed the complete CI in
[run 39027086139](https://github.com/capz/DolphinDotNet/actions/runs/39027086139),
including Windows/macOS/Linux workflows, Rider packaging/Plugin Verifier and real
Dolphin checks. The object/string increment also passed complete CI in
[run 39033281419](https://github.com/capz/DolphinDotNet/actions/runs/39033281419),
including its new real-Dolphin inspection gate. The first overlay CI run
(39045224094) exposed a delayed Dolphin pause notification during disconnect.
The follow-up passes 50 adapter tests, the RSP suite and a local real-Dolphin
object/string, pause and telemetry-memory gate. Current CI status is tracked in
[PR #78](https://github.com/capz/DolphinDotNet/pull/78).
The user has now validated the first interactive Windows Snake workflow in Rider:
attach to a waiting Dolphin, Resume to a dedicated DolphinDotNet source breakpoint,
basic Step Over and primitive values in the Variables window. Step Over was slow
and partial-stack messages repeated; the next increment below addresses those.
Exact installed Rider/plugin versions were not recorded. Full gutter coexistence,
configuration persistence and physical console attach remain separate acceptance
checks. Do not describe fake peers or emulator CI as interactive Rider or hardware
validation.

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
Methods discarded by the native linker remain explicitly unavailable in the
address map, with no invented native extent or breakpoint addresses. Builds still
fail if there are no generated native symbols or no verifiable source addresses.
See [IDE build troubleshooting](ide-workflow.md) for Windows Docker lookup,
interrupted native cleanup and NuGet restore errors.

1. For a sample in Dolphin, select **<sample> in Dolphin** and click **Run**.
   This existing primary configuration now uses **Dolphin Debug**, so it enables
   GDB automatically; **<sample> in Dolphin (wait for Rider)** is an equivalent
   explicit alias. Debug **CLI** / project launches use the same port through the
   shared SDK. The launch profile builds the game and creates
   the emulator settings automatically: GDB port **2166**, interpreter CPU,
   dual-core disabled and debugger mode enabled. It uses a separate `rider-debug`
   profile beneath the configured Dolphin user directory and prints the exact
   `Dolphin.ini` path. No manual INI creation is needed. Existing controller INIs
   seed the debug profile once; later debug changes persist there. The ordinary
   **<sample> in Dolphin (play without debugger)** launch keeps its own settings.
   For a console, start
   the matching debug DOL with its GDB stub waiting/stopped; upload remains external.
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
   selection worked in the user's Snake session; broader coexistence still needs
   acceptance on the pinned Rider.
6. Use **Debug**. Attach performs initialize → attach → initialized → source
   breakpoints → configurationDone. It initially leaves execution stopped. Use the
   standard Rider Resume, Pause, Step Into, Step Over and Step Out controls and
   stack frame source navigation. Stop disconnects the adapter without a target kill.

Attach intentionally starts paused. Dolphin may first stop in native startup code
before the PowerPC stack pointer has been initialized; `Partial stack: InvalidFrame`
at that point is an unwind limitation, not a failed connection. The adapter keeps
the observed native PC as a frame without inventing callers or managed source.
Set a supported source breakpoint (for Snake, `Controller.Update();`) and press
**Resume**. Source navigation and managed inspection become available once that
breakpoint is reached. Step Over/Out at an unsupported native frame may report
an unwind diagnostic; use Resume to reach managed code first.

Step Over now recognizes unconditional PowerPC `bl`/`bla` and `bctrl` calls within
verified managed method extents. It single-steps into the call, then runs with a
temporary return breakpoint and the user's breakpoints installed. A breakpoint in
the callee or at the return address still wins. CFA checks distinguish recursive
activations and preserve same-line stepping. Conditional branches, tail calls and
unrecognized instructions keep the instruction-stepping fallback. A callee that
does not return can keep the step running; **Pause** interrupts it normally. The
step budget counts explicit single-step requests, not instructions executed during
run-to-return. Step Into and Step Out retain their existing instruction stepping.

Debug target builds also expose an optional overlay status slot. After validating
its ELF-bound address, version and signature, the adapter reports connected on
attach and disconnected during clean cleanup. It writes only that reserved state
word; DAP memory/variable mutation remains unsupported. **DEBUG LAST** in the
[statistics overlay](debug-overlay.md) is the last reported state, not a heartbeat:
the guest cannot detect a host-side crash, and a paused game cannot redraw until
it resumes. Unsupported reporting on a stub produces a console note without
preventing debugging; old maps have no reporting side effects.

The adapter reuses a stack snapshot and stable frame IDs until execution resumes,
avoiding repeated register/memory reads for the same stop. Partial-stack warnings
appear once per reason per attach. Rider no longer prints the routine
`SourceLocation` and `UserBreakpoint` descriptions after every successful step;
diagnostic stops and failures remain visible. Pause during an inspection command
waits for that reply instead of injecting an unrelated stop packet into it.

Older bundled adapters can fail after Pause with `Invalid register response:`
followed by an empty value. Dolphin can emit an extra empty GDB packet after the
interrupt stop packet. The adapter now discards that leftover packet before the
next command's acknowledgement. Install a rebuilt Rider plugin to receive this
protocol fix; pulling the repository does not update an installed plugin.

If Resume runs without hitting a breakpoint, open **Run → View Breakpoints** and
check that it is under **DolphinDotNet C# line breakpoints**. A regular .NET line
breakpoint is not sent to this adapter. Select **DolphinDotNet Attach**, then add a
fresh breakpoint on another executable line. Before each Resume the adapter prints
the number of verified source breakpoints and native addresses. Rejected locations
also appear in the console with their source path and reason; check the selected
build folder and source path mappings if the source cannot be resolved.

For that sample launch, choose a saved **BBA / TCP** target at **127.0.0.1:2166**.
Older versions of the target dialog initialized new targets to **2828** even
though the sample launcher uses **2166**. Existing saved targets keep their port;
edit the selected target explicitly rather than assuming a plugin update changes it.
Select the attach configuration before setting a breakpoint, for example Snake's
`Controller.Update();` statement, then Debug and Resume. Dolphin waits at startup
until the debugger connects. Run the launch profile first and leave it running
while starting the separate attach session.

The equivalent host launch option is:

```powershell
dotnet samples/Snake/bin/Debug/net8.0/DolphinDotNet.Build.dll run --config samples/Snake/bin/Debug/net8.0/dolphin-run.json --gdb-port 2166
```

`run --gdb-port` configures Dolphin's emulator stub, independently of the
`DndGdbTransport` build settings used for console-side USB Gecko/BBA stubs.

If attach reports a refused connection, confirm the selected saved target is
`127.0.0.1:2166` and the **wait for Rider** launch remains running. Its command must
include `--gdb-port 2166`. On Windows, inspect the listener without connecting a
second debugger:

```powershell
Get-NetTCPConnection -State Listen -LocalPort 2166 -ErrorAction SilentlyContinue |
    Select-Object LocalAddress, LocalPort, OwningProcess
```

No result means no listener is present on that port. Inspect the launch console's
printed settings path and Dolphin process command line to confirm the intended
user profile is active. The ordinary launch profile does not enable GDB.

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

## Stopped inspection contract

The compiler joins Portable PDB parameter/local names and lexical IL scopes to
generated native slots. The address resolver accepts verified CFA-relative
locations and PowerPC register locations, including bounded DWARF location lists.
Values are decoded as big-endian Boolean, Char, signed/unsigned integers, native
integers, Single or Double. Caller frames permit proven CFA-relative memory only;
register-backed values are restricted to the top frame. Missing, ambiguous or
unsupported locations display an explicit unavailable reason. No expression is
evaluated to recover a value. Scope visibility is restricted to proven mapped
statement addresses.

The next slice exposes reference arguments, locals and class `this`. Supported
non-generic classes expand into scalar fields, string previews and nested class
references in Rider's existing Variables window. Inherited fields use the same
payload offsets as generated field accesses; hidden names are qualified by their
declaring type. Null displays as `null`. The runtime type pointer must match a
type symbol bound from this exact ELF. Missing/discarded symbols, native/generic
base layouts, invalid pointers and unknown runtime types are unavailable rather
than interpreted using a guessed layout. Scalar fields use their actual 1/2/4/8
byte width; reference fields reserve the compiler's payload space but read the
PowerPC's four-byte pointer.

String previews read at most **128 UTF-16 code units**, escape controls/surrogates,
and show the total length when truncated. Reference expansion stops at depth 4,
including cyclic graphs. The adapter returns at most 128 variables per request;
Rider exposes larger objects through page groups. Layouts are limited to 4096
fields, and at most 4096 expandable handles are retained per stop. Handles expire
on any execution. Member reads are lazy and validate the MEM1 bounds before
reading; a well-framed target memory refusal becomes an unavailable value while
other fields and the session stay usable. Corrupt transport responses still fail.

Arrays, value-type expansion, static fields, properties, expressions and mutation
remain unsupported. A struct field is explicitly unavailable; generic/unsupported
runtime layouts are not expanded. No getter, `ToString`, allocation or other target
code is invoked for inspection.

**Rebuild the target Debug build and install the rebuilt plugin ZIP** for object
inspection. Older maps still work for their existing primitive slice, but do not
contain the reference slots/type layouts required for object expansion. The
optional inspection sub-schema is version 1 inside the existing source/address
maps; their outer schemas and file names are unchanged. Generated Debug C asserts
the PowerPC32 object/string ABI before metadata can be used.

## Acceptance evidence and remaining checks

Automated host checks:

- `tests/test_debug_adapter.py`: real adapter subprocess with fake RSP TCP target;
  protocol framing, source identity/maps, breakpoint ownership, stack/stepping,
  pause/resume/disconnect, primitive values, pagination, expired handles, invalid
  transports and serial enumeration.
- Object tests cover lazy members, scalar widths, nested/null/cyclic references,
  UTF-16/truncated strings, invalid references/lengths, missing and malformed
  layouts, target memory refusals, paging, unsupported arrays/struct fields and
  expired object handles. Compiler tests assert inherited offsets and reference
  slots; the resolver binds linked type symbols only. Java TCP/PTY integration
  exercises roots, child pages and stale handles through the packaged adapter.
- Fast-Step-Over tests cover direct/indirect calls, recursion, same-line returns,
  user breakpoints inside a callee and at its return, conditional-call fallback,
  Pause/disconnect cleanup, target exit/signals, a Pause during code reads, stack
  snapshot invalidation and deduplicated unwind diagnostics. A supported call uses
  one single-step plus one continue instead of stepping its entire body. These are
  protocol/command-count checks; real emulator speed and the rebuilt Rider plugin
  require their own acceptance result.
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

To complete A6 acceptance beyond the reported Snake workflow, record the exact installed Rider/plugin versions,
configuration save/reload and Cancel behavior, BBA/USB transport field switching,
serial refresh/error handling, dropdown selection, C# gutter coexistence, source
navigation, pause/resume, stack selection and all three step controls against the
real Dolphin fixture. Verify Stop leaves the emulator alive and no owned breakpoint
installed. Keep A4.4 physical BBA and USB Gecko validation deferred separately.

Out of scope: DAP launch/build/upload orchestration, automatic target discovery or
slot negotiation, verified hardware compatibility, reconnect, arrays/value types,
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
