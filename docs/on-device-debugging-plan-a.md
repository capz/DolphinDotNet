# Plan A: on-device GameCube native GDB debugging

Status: **A1–A4.3 implemented and emulator CI validated** (run 38910696313).
A4.5 source stepping is in progress. A4.4 physical USB Gecko/BBA validation is
**deferred: the user currently has no access to hardware**. Continue emulator,
DAP and Rider milestones without requiring hardware validation first. Physical
attachment remains unverified; host TCP/serial transports are implemented.

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
