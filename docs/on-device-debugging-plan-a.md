# Plan A: on-device GameCube native GDB debugging

Status: **increments A1, A2a, A2b and A2c (generated C source directives) implemented**.
PowerPC breakpoint-address extraction and IDE attach are **not yet implemented**.

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
| A3 | Emit precise source debug locations and verify PowerPC address mapping using ELF/DWARF; handle multi-address source lines | Host lookup for C# file/line -> unique/ordered native breakable addresses, independently tested with `readelf` and `objdump` |
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
