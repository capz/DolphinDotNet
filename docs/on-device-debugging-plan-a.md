# Plan A: on-device GameCube native GDB debugging

Status: **A1–A4.3 implemented and emulator CI validated** (run 38910696313).
A4.5 source stepping is in progress. A4.4 physical USB Gecko/BBA validation is
**deferred: the user currently has no access to hardware**. Continue emulator,
DAP and Rider milestones without requiring hardware validation first. Physical
attachment remains unverified; host TCP/serial transports are implemented.
The first interactive Rider/Dolphin Snake workflow is now user-validated: attach,
Resume to a DolphinDotNet source breakpoint, basic Step Over and primitive values.
Further A6 checks and A7 object inspection remain incremental work; see the current
stepping/diagnostics increment at the end of this document.

## Repository and dependency audit (2026-10-16)

- Managed compilation: `tools/DolphinDotNet.Compiler`, with `AotCompiler`,
  `ValueIrImporter`, `ValueCBackend`, and `GameCubeBundle`.
  `ValueIrBlock.CilOffset` and A2a's per-instruction `ValueIrInstruction.CilOffset`
  now survive lowering. A2b reads matching Portable PDBs and resolves visible
  source sequence points to emitted IL offsets; PowerPC mapping remains outstanding.
- Build route: shared MSBuild SDK -> `DolphinDotNet.Build` ->
  `dndc --bundle` -> Makefile -> devkitPPC/ELF -> DOL.
  The driver uses a pinned libogc2 container digest or a local toolchain.
  The `.elf` is retained by normal builds.
- Device debugger: libogc2's `libdb.a` exposes `DEBUG_Init`, `_break`,
  TCP/BBA and USB Gecko transports; TCP needs explicit static IPv4 settings
  and runs its own uIP stack. `-ldb` was not linked before A1.
  Source audited at `extremscorner/libogc2/libdb/debug.c` and `include/debug.h`.
- Emulator: `scripts/dolphin_gdb_probe.py` already implements breakpoint
  insertion, continue, register/memory reads and stop packet verification
  against Dolphin Emulator's GDB service. Reuse or extract its RSP implementation.
- Limitations: current libdb debugger has a 2 KiB packet buffer and 256
  software breakpoint slots; noncurrent-thread register writes are incomplete.
  Its polling TCP/BBA service may conflict with normal libogc2 networking.
  Neither Rider nor Visual Studio automatically attaches to that service.

## Increments and acceptance tests

| Increment | Implementation | Acceptance criteria |
| --- | --- | --- |
| A1 (this change) | Opt-in TCP GDB bootstrap, fixed static IPv4, `-ldb` linkage, debugger-friendly ELF flags, early `_break()` | Default bundle unaffected; generated C/Makefile checks; PowerPC linker CI proves libdb linkage and DWARF sections |
| A2a (this change) | Preserve per-instruction CIL offsets on Value IR instructions and terminators; write deterministic `dnd-il-debug.json` in the native bundle when GDB enabled | Normal bundle unaffected; entry CIL offsets sorted and unique; compiler integration test |
| A2b (this change) | Load and verify matching Portable PDB identity for each reachable assembly; correlate source sequence points with emitted IL offsets | Multiple statements in one basic block retain distinct source lines; missing/stale PDB fails explicitly; deterministic host-only JSON |
| A2c (this change) | Emit native C `#line` directives from verified A2b source locations | Generated C has per-statement source lines, default builds unchanged; ELF DWARF line records checked by CI |
| A3 (this change) | Verify ELF DWARF line addresses within native function symbol extents and publish address sidecar | Parser regression tests, GCC DWARF fixture, PowerPC ELF CI validation, explicit ambiguity/mismatch failures |
| A4 | Extract host RSP client from existing Dolphin probe; target capability discovery and breakpoint lifecycle | Emulator breakpoint/continue/step and stop-PC tests pass; limited libdb command set tested against hardware |
| A5 | Build host C# debug adapter implementing DAP, native-symbol map and source-level stepping | DAP protocol tests with fake RSP target; VS Code/Visual Studio integration spike; correct source stack locations |
| A6 | Rider/Visual Studio installation and attach packaging | Both IDEs set C# breakpoints, pause/resume and step on emulator and a physical console |
| A7 | Local inspection, crash diagnostics, async call stack improvements and robustness | Debugging errors recovered gracefully; explicit unsupported-variable behavior and detachment tests |

## Using increment A1 on physical hardware

Build from the IDE/CLI with these MSBuild properties (example addresses are
documentation-only; configure your *actual* GameCube LAN before running):

```sh
dotnet build samples/HelloGameCube/HelloGameCube.csproj -c Debug \
  -p:DndGdbTransport=Tcp \
  -p:DndGdbIp=192.168.1.80 \
  -p:DndGdbNetmask=255.255.255.0 \
  -p:DndGdbGateway=192.168.1.1 \
  -p:DndGdbPort=2828
```

Alternatively, with an already-built managed assembly:

```sh
dotnet run --project tools/DolphinDotNet.Compiler -- \
  --bundle path/to/Game.dll path/to/output \
  --gdb-tcp 192.168.1.80 --gdb-netmask 255.255.255.0 \
  --gdb-gateway 192.168.1.1 --gdb-port 2828
make -C path/to/output
```

Boot the DOL on a GameCube with a Broadband Adapter. The runtime initializes
libogc2's stub immediately after platform initialization and enters `_break()`
before normal managed execution. Attach with a suitable PowerPC GDB client
using the corresponding `.elf`:

```sh
powerpc-eabi-gdb path/to/Game.elf -ex 'target remote 192.168.1.80:2828'
```

Then use GDB's native symbols, breakpoint and continue commands. This is **not**
yet C# IDE debugging; there is currently no C# debug source map or DAP adapter.

The GameCube debugger uses a separate polling network stack. A1 disables
automatic application network initialization while the debug stub is enabled;
manual managed network use and debugger coexistence have **not** been validated.
Do not expose the unauthenticated debugger port to the Internet. Prefer an
isolated development LAN.

## Risks and tests still requiring real hardware

1. **Startup/liveness:** verify BBA wiring, local static addresses, `_break`
   actually waits for GDB and that `continue` resumes the DOL.
2. **Networking:** verify debugger connection survives after normal runtime
   services initialize; determine whether app networking must be explicitly
   disabled for the session.
3. **Exception/stepping:** exercise libdb's PPC exception hooks, instruction
   stepping, breakpoints inside generated native methods and detach.
4. **Transport:** verify the installed pinned libogc2 archive exports the
   expected stub and that the client accepts its stop reply format.
5. **IDE:** Rider custom debugger plugin support must be prototyped rather
   than assumed; Visual Studio will require a DAP extension/host.

The GDB-enabled bundle also writes `dnd-il-debug.json`, recording the reachable
generated native function symbols, per-instruction IL offsets and A2b's
matching Portable PDB source locations. It also includes assembly MVID and PDB
identity for host-side build validation. This source map is **not yet mapped to
PowerPC addresses**; A2c/A3 must generate and verify native line tables.
The debug metadata is also published alongside the ELF as
`<AssemblyName>.dnd-debug.json` by SDK builds, so a future debugger adapter
does not need to inspect intermediate build directories. It is never deployed
on-device. Normal builds remove any old sidecar.
Debug builds fail if a reachable assembly's Portable PDB is missing or stale.
The generated C additionally receives `#line` directives in debug builds only,
including resets around hidden IL and synthetic native instructions. GCC uses
these directives to generate C# file/line records in ELF DWARF; **A3 must still
extract and validate breakable PowerPC instruction addresses**.

Do not claim source-level breakpoints or live hardware compatibility until the
later acceptance tests pass.

## A3 native breakpoint addresses

GameCube GDB builds generate `Game.dnd-addresses.json` after the ELF link. The
build driver publishes `<AssemblyName>.dnd-addresses.json` next to the ELF.
The table includes native function symbols, managed method tokens, C# document
and line, original IL offsets, and verified PPC instruction addresses. It
records SHA-256 hashes of the source-map input and ELF to allow a host adapter
to reject mismatched artifacts. The resolver discards unaligned, zero and
out-of-function addresses and rejects ambiguous source basenames. Optimized
source statements may lack an address or share one with another statement.
This is host-side lookup metadata, **not** a functioning IDE debug adapter.

## A4: emulator C# source-breakpoint probe

Use `-p:DndDebugSymbols=true` (or `dndc --bundle ... --debug-symbols`)
to emit PDB-based C# file/line addresses without libogc2 `libdb` or an early
device-side `_break()`. `DndGdbTransport=Tcp` still implicitly requests
debug symbols when targeting actual GameCube hardware.

CI builds a source-symbols-only DOL and boots it in Dolphin Emulator with the
emulator's own GDB service. A source breakpoint host probe checks ELF and
source-map SHA-256 identities, installs native breakpoints for two C# lines,
and requires stopped PowerPC PCs to match those addresses. This verifies
source breakpoints but does not yet provide managed stepping, DAP or an IDE.

## A4 transport extension: USB Gecko

USB Gecko is supported as a separate **physical target transport** sharing
libogc2's native `libdb` GDB protocol and the same managed source/ELF maps as
TCP. It does not require a Broadband Adapter or configured IP address.

- The compiler accepts `--gdb-usb 0` (memory-card slot A) or `--gdb-usb 1`
  (memory-card slot B). TCP and USB arguments are mutually exclusive.
- The MSBuild SDK accepts `DndGdbTransport=UsbGecko` and
  `DndGdbChannel=0|1`, defaulting to channel 1 / slot B.
- USB builds link `-ldb`, initialize
  `DEBUG_Init(GDBSTUB_DEVICE_USB, channel)`, then call `_break()`.
- USB mode **does not force-disable application networking**. Automatic
  networking remains driven by the original usage-based build configuration;
  unlike the BBA TCP stub, it does not reserve the network stack.
- CI verifies both USB and TCP GDB executables link and contain DWARF/address
  metadata. **CI does not prove host serial connectivity or real hardware**.

Example:

```sh
dotnet build samples/HelloGameCube/HelloGameCube.csproj -c Debug \
  -p:DndGdbTransport=UsbGecko -p:DndGdbChannel=1
```

The GameCube must have a compatible USB Gecko in the selected slot. Host-side
PowerPC GDB can be used with its supported serial device name, for example
`target remote COM5` on Windows or `target remote /dev/ttyUSB0` on Linux.
These are examples, not discovered ports; serial permissions, USB adapters,
GDB framing, detach and reconnect **require hardware testing**.

### Remaining tasks (updated)

1. **A4.1 (implemented, CI passed):** opt-in USB Gecko init, strict channel
   validation, symbols and PowerPC linking, shared debugger path.
2. **A4.2 (implemented, CI passed):** reusable .NET RSP client with pluggable TCP and serial transports.
   Specify UART/FTDI settings from hardware tests rather than assuming baud.
3. **A4.3:** emulator RSP breakpoint/step/continue tests; regression checks on
   PC and breakpoint lifetimes, including target capability differences.
4. **A4.4 (deferred: no hardware available):** USB Gecko slot A/B, BBA TCP attach, break/step,
   disconnect/reconnect, network coexistence, serial-port drivers and permissions.
5. **A4.5:** native C# source-aware stepping, managed method frame mapping,
   helper-frame filtering, optimized/hidden sequence-point handling.
6. **A5:** DAP adapter exposing attach/launch, source breakpoints, stepping
   and managed call stacks using a single backend.
7. **A6:** Rider integration first (Visual Studio VSIX optional later), emulator and physical
   console launch/attach configurations; mock up any custom UI first.
8. **A7:** locals/arguments/objects, exceptions, async diagnostics and
   resilience. Keep unsupported features explicit rather than fabricating state.

**Delivery order:** First emulator source breakpoints, stepping and DAP; second
Rider integration; third USB Gecko and TCP physical debug proof; fourth
managed inspection and reliability. Prototype Rider compatibility early during A5. No claim of physical USB Gecko debugging
until a real-device serial session passes the acceptance suite.

## A4.2 .NET host RSP transport foundation

A new `tools/DolphinDotNet.Debugger` .NET 8 library implements RSP
packet checksums and acknowledgements (including checksum retransmission),
TCP connection, explicitly configured serial-port connection for USB Gecko,
software breakpoints, stop/continue, instruction stepping, memory/register
reads, and asynchronous interruption. It accepts arbitrary duplex `Stream`
instances so the protocol can be exercised without a physical GameCube.

The USB Gecko host serial baud rate is **deliberately not hardcoded**: it must
be supplied explicitly and verified with real FTDI hardware and driver setup.
`System.IO.Ports` supplies the serial port adapter, not a USB Gecko EXI driver.
TCP and serial share one RSP client, but legacy libdb may not implement
qSupported or stop-register reporting. Emulator and hardware feature profiles
must be validated separately before an IDE adapter is claimed.

`tests/DebuggerTests` uses a loopback fake GDB server to validate packet
framing, stop PCs, breakpoint lifecycle, checksum retry, memory/register
reads and cancellation. No custom debug UI or DAP support is in this increment.


## A4.3 .NET source breakpoint integration

`SourceAddressMap.Load` verifies ELF/source-map SHA-256 identity and schema
versions before resolving C# lines. Document suffixes match path components;
ambiguous basenames and unmapped lines fail explicitly. All aligned native
addresses for a source line are deduplicated. `SourceBreakpoints` tracks native
installations, including successful partial writes and failed removals, so callers
can retry cleanup while the target remains stopped. Calls must be serialized by
the session owner; this layer does not yet implement per-IDE breakpoint ownership.

`tools/DolphinDotNet.Debugger.Probe` is a CI acceptance executable. The existing
Dolphin smoke harness can invoke it with `--dotnet-probe PATH_TO_DLL`. The probe
must hit two distinct C# source PCs using the .NET client, remove all breakpoints,
advance one native instruction, then interrupt a pending Continue and read the
stopped PC (falling back to big-endian PowerPC register 0x40 if necessary).
The harness owns emulator termination; the probe leaves the guest stopped.

A4.3 passed real-Dolphin CI in run 38910696313. Native instruction stepping
was verified separately from the A4.5 source-step increment below.
Hardware, DAP, Rider integration and variable inspection remain separate gates.


## A4.5 first increment: bounded source Step Into

`SourceAddressMap.FindExact` maps a stopped PC only at verified DWARF addresses,
preserves original document case, and joins method symbols to the matching
managed metadata for display names. It retains all locations for ambiguous PCs;
it never invents a source line for an unmapped native instruction.

`SourceStepper.StepIntoAsync` advances native instructions until reaching another
mapped C# line or method. Multiple native addresses on the same line are skipped,
as are unmapped native helpers. A finite instruction budget, cancellation, target
exit, unexpected signals and ambiguous mappings all have explicit outcomes. The
caller must serialize session operations and remove breakpoints before stepping.
This initial engine does not arbitrate existing user breakpoints or reconstruct
managed recursion depth, async continuations, inline frames or multiple statements
on one line. Long native calls may exhaust the budget while still stopped in
native code. Retrying a bounded step is allowed; no unbounded execution is hidden.

The Dolphin .NET acceptance probe now requires a mapped source-step result and
prints method/file/line/PC plus the number of native instructions. Fake-target
regressions cover same-line addresses, native helpers, managed calls, fallback
register reads, ambiguous source locations, budget limits, crashes, exits and
cancellation. Real-Dolphin CI remains required for this new increment.

Next: managed stack unwinding and reliable call-depth tracking for Step Over/Out,
then DAP and Rider integration. **A4.4 remains deferred until hardware is available.**


## A4.5 second increment: DWARF stack frames and Step Over/Out

The address resolver now retains managed native-symbol extents and interpreted
PowerPC DWARF call-frame rules. Its supported subset covers CFA register plus
constant offset, same/undefined registers, register-to-register saves, and
CFA-relative memory saves. It preserves unsupported rows as explicit gaps;
no prior row is extended across an unsupported expression. FDEs without their
own rows inherit the matching PowerPC CIE. Debug builds additionally disable
sibling-call optimization to keep generated managed callers visible.

`PowerPcUnwinder` reads 32-bit big-endian PowerPC state. DWARF LR register 65 is
translated to GDB register 67; CFA registers retain their general-register
numbers. Caller methods/source locations use the return PC minus four to refer
to the PowerPC call instruction. Frame recovery is bounded and validates
MEM1 pointers, checked address arithmetic and repeated PC/SP pairs. Missing CFI,
unsupported register recovery and invalid frames return partial stacks with an
explicit reason. Unmapped native frames are retained, not labeled as managed.
This is not a general DWARF-expression evaluator or exception/async unwinder.

Step Over accepts a new mapped statement in the same CFA or after returning to
a greater CFA, and skips mapped statements in deeper frames. Step Out waits for
a mapped statement with a greater CFA. Both stop with `UnwindUnavailable` when
needed rules are missing rather than guessing a return address. Native signals,
limits and ambiguity retain the behavior of bounded Step Into. All calls require
a stopped target, serialized session control and removed breakpoints. Arbitrary
fiber-stack switches, user breakpoint arbitration and advanced optimized frames
remain unsupported.

`tests/DebuggerStackSmoke` supplies a dedicated Main -> Outer -> Inner program.
A separate real-Dolphin probe must show these three managed frames, step out of
Inner, then step over a second Inner call while staying in Outer. Fake RSP tests
exercise saved LR/r31 recovery, leaf LR translation, caller-source attribution,
partial stacks, CFA depth comparisons and invalid/missing unwind information.
The parser also has prologue/body/epilogue and CIE-inheritance regressions.
These new behaviors require their own passing CI run; A4.4 remains deferred.

## A5 first increment: attach-only DAP process

A4.5's original run 38916826744 recorded a probe failure before being superseded.
Step Out had already reached Outer:14; two unconditional Step Over calls overshot
Outer:15. Commit f2744a8 made that assertion source-position-aware and CI run
38919479273 passed, including Inner -> Outer -> Main and Step Out/Over evidence.
This does not validate physical BBA or USB Gecko; A4.4 remains deferred.

`tools/DolphinDotNet.DebugAdapter` now exposes the verified source maps and host
RSP backend through a single-session stdio DAP process. It attaches to an existing
Dolphin TCP target, replaces source breakpoint sets, maps source paths, provides
bounded call stacks and source stepping, and supports continue/pause/disconnect.
Native breakpoint ownership is shared across source sets; stepping checks desired
user breakpoint PCs before skipping deeper calls. Continue is no longer constrained
by an ordinary RSP command timeout. Uncertain protocol failures terminate the
session rather than reusing a possibly desynchronized reply stream.

New black-box host and real-Dolphin DAP CI checks are required to validate this
increment. Rider hosting remains a separate explicit gate; the documented LSP4IJ
route is only a candidate and currently documents a Pause limitation. No custom UI,
physical compatibility, variable inspection or Rider release is claimed.
See `docs/rider-debugging.md` for attach arguments, responsibilities, acceptance
commands and remaining integration gates.

### A5 source-step CI correction

Run 38930010375 exposed equal-PC DWARF views at 0x80005168: the resolver
incorrectly retained both ManagedGameCube line 9 (empty extent) and line 10
(the final effective row). The resolver now respects row ordering and exclusive
end-sequence boundaries, including superseding hidden/native rows. It never
chooses between genuinely overlapping independent sequences.

Debug C generation now emits a volatile NOP source rendezvous before visible IL
instructions, with a memory clobber, so optimized-away assignments/returns still
have an executable source position. These markers affect debug builds only;
they add code size and stepping work, not runtime service dependencies. The
source directive is repeated after each marker to avoid shifting C# line numbers.

DAP acceptance now requires `SourceLocation`, not merely a generic `step` event:
ambiguity, instruction limits and missing unwind rules remain diagnostic stops.
Both source probes step from the first short statement before the rendering call;
the stack probe accepts returning to the current caller statement before stepping
to the second call and proving Step Over. New CI is required for the corrected
PowerPC maps and full DAP source/pause/stack flow. A4.4 remains deferred.

## A5 emulator gate passed; A6 host feasibility checked

CI run [38934685398](https://github.com/capz/DolphinDotNet/actions/runs/38934685398)
passed all selected checks on aa63a0499e25fe0c98ba31b6205d792218ee3b5f, including
real-Dolphin DAP source breakpoints, source stepping, pause, managed stack and
Step Over/Out. The DWARF correction is now emulator validated.

The initial Rider-host source audit found that LSP4IJ's documented/current
implementation lacks Pause. Its separate DAP breakpoint type also requires an
interactive C# gutter-routing test. See the pinned upstream source references and
A6 decision in `docs/rider-debugging.md`. Next: thin Rider bridge feasibility,
starting with breakpoint routing and pause; any custom UI needs an approved
mockup first. Rider itself remains unvalidated and A4.4 stays deferred.

## A6 first implementation: Rider attach and saved targets

The user approved the native Rider configuration and Manage targets modal mockups.
`plugins/rider` now provides a run configuration with a debug-build folder, resolved
artifact rows, saved-target dropdown, target manager and source prefix mappings.
Machine-local profiles support BBA/TCP (also Dolphin) and USB Gecko VCP; stable IDs
keep run configurations independent of profile renames. USB slot A/B is expected
build metadata only. Host attach cannot change the running DOL's EXI channel.

The plugin uses public XDebugger APIs and LSP4J DAP over stdio to the packaged .NET
adapter. The bridge implements source breakpoint replacement/verification, initial
stopped attach, pause/resume, managed stack locations and Step Into/Over/Out using
the existing adapter. It gates its C# gutter breakpoint type on selection of a
DolphinDotNet configuration, preserving normal .NET breakpoint ownership. SDK target
is Rider 2026.2.3.1 / build 262 with Java 25; support is bounded to 262.*.

The adapter now accepts explicit USB serial/driver compatibility settings as well
as TCP, and exposes a separate JSON serial-port enumeration mode. Host tests include
actual System.IO.Ports traffic over a Linux PTY to a fake RSP target. Java tests
exercise saved profiles, artifact discovery and the LSP4J lifecycle with a fake DAP
process. Neither test substitutes for interactive Rider or real FTDI/EXI hardware.

Validation at implementation time: SDK javac compilation, 6 Java tests, 11 adapter
tests and 18 CI-selection tests passed. Complete Gradle packaging is pending CI;
local Gradle dependency resolution is blocked by this environment's Java networking.
The dedicated `Rider attach plugin` job builds/tests/packages the pinned SDK and
uploads the plugin ZIP. See `docs/rider-debugging.md` for the exact install/attach
procedure and manual acceptance checklist. At that stage A6 was unvalidated interactively;
A4.4 physical USB Gecko/BBA remains deferred. Do not infer either from emulator CI.


## A6 reliability and first A7 primitive slice

PR #77 merged after [CI 38944870062](https://github.com/capz/DolphinDotNet/actions/runs/38944870062)
passed, including Gradle packaging, host TCP/PTY and real-Dolphin DAP acceptance.
The follow-up keeps the approved saved-target UI and uses Rider's existing Variables
pane; no new custom UI is introduced. Debugger target builds are managed **Debug**
with Portable PDBs. Release optimization remains unchanged and Release-mode
debugging is not supported by this acceptance scope.

- A6 lifecycle: bounded startup/disconnect, cancellation and failed-attach cleanup,
  adapter exit/stdout EOF notification, and queued execution guards. Cleanup owns
  the adapter process only; the target is never killed.
- A6 packaging: Plugin Verifier against the pinned Rider SDK, and the Java DAP
  client against the real packaged .NET adapter over TCP and Linux PTY fixtures.
- A4.5 robustness: CFA transitions distinguish recursive/same-line calls; user
  breakpoints retain priority and desired-breakpoint hit lookup is indexed.
- A7 primitives: Portable PDB names/scopes → generated native slots → conservative
  DWARF register/CFA locations → big-endian values → DAP scopes/variables → native
  Rider Variables pane. References expire on resume; unsupported values are explicit.
  Real-Dolphin acceptance additionally checks Inner's argument and Outer's local.

Relevant implementation: `PortablePdbSourceMap.cs`, `ValueCBackend.cs` and
`GameCubeBundle.cs` in the compiler; `scripts/dnd_debug_addresses.py`; debugger
`PrimitiveVariables.cs`, `SourceAddressMap.cs` and `SourceStepper.cs`; adapter
`DebugSession.cs` and `BreakpointRegistry.cs`; Rider `DapClient.java` and
`RiderDebugProcess.java`. Host/protocol tests, Python DWARF/adapter tests, Java
integration/fault tests and emulator probes cover these responsibilities.

Remaining acceptance outside this environment is interactive Rider on the pinned SDK:
C# gutter coexistence, configuration persistence, target management, navigation,
step controls and Variables presentation. Physical A4.4 USB Gecko/BBA remains
deferred separately. Neither is proven by fake peers, Plugin Verifier or emulator
success. Objects, evaluation, mutation, exception/async diagnostics, Release target
builds, automatic reconnect and Visual Studio remain outside this slice.

## Interactive Snake validation and faster Step Over

On 2026-10-17 the user confirmed a Windows Rider session with Dolphin waiting for
attach, Resume reaching a dedicated DolphinDotNet source breakpoint, working basic
Step Over and primitive values in Rider's Variables window. Regular .NET line
breakpoints were not forwarded; selecting the DolphinDotNet attach configuration
and using its breakpoint type resolved that issue. The exact installed versions
and commit were not supplied, so this records the working flow rather than a full
versioned A6 release gate. Pause also exposed Dolphin's extra empty interrupt reply;
the protocol fix and regression are in the follow-up branch.

The next increment addresses the reported slow stepping and console repetition:

- Supported unconditional linked calls use a temporary return breakpoint for
  Step Over. Desired user breakpoints stay active during the continue, including
  recursive calls. CFA/source arbitration still decides when the step completes.
- Conditional/tail/unknown calls retain instruction stepping; unsupported unwind
  states stay explicit. Step Into/Out are unchanged. Pause interrupts a long callee.
- Pause is serialized with a running continue, avoiding unsolicited stop replies
  during memory/register commands. Early Pause requests are checked when continue
  starts and during instruction stepping; disconnect uses the same stop path.
- Stack snapshots/frame IDs are reused while stopped and expire on execution.
  Partial-stack warnings are emitted once per reason per attach. Routine successful
  source-step/breakpoint descriptions are no longer printed by the Rider bridge.

Host regression coverage exercises the real adapter with deterministic RSP peers
for call/return, recursion, same-line calls, breakpoint priority, interruption,
cleanup, exit/signals and stack snapshot lifetime. The existing real-Dolphin stack
gate steps over Inner in Outer and remains required for this increment. Command
count reduction is tested; real latency and updated Rider UI behavior still need
acceptance. This stepping increment passed complete CI in
[run 39027086139](https://github.com/capz/DolphinDotNet/actions/runs/39027086139).
The following inspection increment builds on that validated baseline.

## A7 bounded class and string inspection

Reference arguments, locals and class `this` now have host-side metadata alongside
the primitive slice. The compiler exports supported class field layouts using the
same base/payload offsets as code generation. The native resolver binds these to
linked type objects; only an exact runtime type pointer match permits expansion.
PowerPC32 object/string ABI assertions protect the emitted layout. Old maps keep
their primitive behavior; target Debug maps must be rebuilt for reference slots.

Rider's existing Variables window lazily expands class fields and nested references.
It displays nulls and bounded UTF-16 string previews without invoking getters or
`ToString`. Depth 4, 128 values per page, 128 UTF-16 units per string preview and
4096 object handles per stop bound the work. Cycles terminate at the depth limit.
References expire on execution. Invalid pointers/layouts and unsupported array or
struct fields are explicit; a valid target memory refusal affects that value,
while corrupt RSP replies still fail. Generic/native base layouts, arrays,
value-type expansion, statics, properties, evaluation and mutation are outside
this slice.

Local coverage includes compiler metadata and inheritance assertions, resolver
symbol validation, real-adapter object/string/pagination/lifetime tests and an
actual PowerPC fixture build. The Java TCP/PTY assertions and real-Dolphin
inspection gate passed complete CI in run 39033281419. Neither CI nor fake peers
prove the new Rider object UI. The Dolphin fixture checks object arguments/locals,
inherited scalar fields, a nested/cyclic reference and string previews. Interactive
object expansion remains a separate user check after installing the rebuilt plugin
and rebuilding the target's Debug artifacts.

## BBA and debugger statistics overlay

The existing performance panel now adds BBA configuration state/mode and IPv4,
netmask and gateway after configuration is attempted. Its read-only platform
snapshot never initializes BBA/DHCP or polls sockets. The separate TCP GDB stub
publishes its own static configuration, explicitly distinguished from application
socket readiness and Dolphin's host GDB listener.

Debug symbol builds retain a versioned eight-byte overlay telemetry slot. The
native map binds its exact symbol; the adapter verifies the signature before
writing the four-byte connected/disconnected state on attach/clean disconnect,
then reads the state word back. An acknowledged write that does not persist is
reported as unavailable without failing attach.
Old maps do not cause writes, and unsupported reporting does not prevent attach.
Variable/expression mutation remains unsupported. The row is labelled DEBUG LAST:
it is a last reported state, not a heartbeat. Abrupt host failure cannot be detected
from the guest, and a stopped target does not redraw until it runs another frame.

Portable overlay tests cover five/nine-row layouts, status/IPv4 formatting and
both reporting/non-reporting builds. Host tests cover signature refusal, absent
metadata, unsupported/ineffective writes and running disconnect cleanup. The
real-Dolphin DAP inspection gate retains the upstream test socket after the adapter
closes it, then independently requires the disconnected state and intact signature
in guest RAM. The relay forwards bytes unchanged during the adapter's session and
never issues concurrent RSP commands. Host tests exercise both successful relay
readback and rejection of a missing report. A native PowerPC
build verifies snapshot compilation and the retained slot's exact ELF bytes.
Physical BBA behavior and visual acceptance remain separate checks.

Connectivity snapshots now use a separate 2,000 ms timer while the performance
panel is visible. Showing the panel invalidates the cached link and requests a
fresh sample; hidden frames perform no link polling. Memory statistics retain
their independent 250 ms cadence. For the original Nintendo BBA (EXI ID
0x04020200), a read of NWAYS 0x31 observes its 10/100 link bits under a non-waiting
EXI lock. No network initialization, socket probe, DHCP request or adapter reset
is performed. Unsupported hardware, contention and failed reads are unknown,
not an inferred link-up; configured IP details survive cable loss. Tests cover
the timer boundaries, toggle/clock behavior, register command, contention and
failure cleanup, and both normal and reporting overlay layouts.

The requested two-second heartbeat interval is reserved for the next debugger
transport slice. Live debugger-loss detection is not implemented by these link
checks: Dolphin's host GDB connection is independent of guest BBA state, and the
current RSP session has no separate heartbeat channel while execution is active.
DEBUG LAST keeps its existing, explicit last-report semantics. Running-target
heartbeat support needs transport work and hardware/guest-network validation.

The first overlay CI run (39045224094) exposed a Dolphin pause/cleanup race:
its CPU pause loop can send a second SIGTRAP after the Ctrl-C stop, at a later PC
and after an intervening inspection reply. The RSP client now drains at most one
such pending pause notification, plus the previously supported empty interrupt
reply, before a command's ACK. The window ends when execution resumes. Other
signals, repeated traps and traps after that ACK still fail rather than concealing
protocol corruption. Regression tests cover both reply orders, delayed delivery
and these refusal boundaries; the real-Dolphin telemetry gate exercises pause
followed by cleanup.
