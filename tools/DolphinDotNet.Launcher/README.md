# DolphinDotNet GameCube ELF launcher

A small standalone native GameCube program that receives an ELF over BBA/TCP or
USB Gecko and starts it. It needs libogc2 and the platform libraries already used
by DolphinDotNet. It does not require Swiss to remain running, a desktop CLR on
the console, wiiload, zlib, a filesystem, or the managed runtime.

Boot `DolphinDotNet.Launcher.dol` once using your existing homebrew boot method.
Leave it running while developing. In either debugger plugin choose
**DolphinDotNet ELF launcher (BBA / USB Gecko)**, select the Debug build folder
and target, and choose **Check receiver readiness**. The check exchanges no
executable bytes and releases its session afterward. Launch always checks again,
waits for the receiver if needed, uploads the ELF, requests execution after
validation, then attaches to the application's GDB stub.

The console reports BBA configuration, its upload address, the IDE handshake,
readiness to receive, validation and execution. BBA configuration runs on a
separate thread so USB Gecko remains available when no network is connected.
Both Gecko slots are checked. One transfer is served at a time. The application
must be built with a waiting GDB stub for the selected transport/EXI slot. It
owns its network/GDB settings after handoff; the launcher does not rewrite them.

## Build and boot

To download a prebuilt DOL, open a successful **GameCube ELF launcher** run in
[GitHub Actions](https://github.com/capz/DolphinDotNet/actions/workflows/launcher.yml)
and download **DolphinDotNet-ELF-launcher** from its **Artifacts** section (GitHub
sign-in required). Extract `DolphinDotNet.Launcher.dol` from the archive and boot
it using your existing homebrew boot method. These builds use the default DHCP
and port settings below. You can also start a build with **Run workflow**.

With devkitPPC and libogc2 configured:

```sh
make -C tools/DolphinDotNet.Launcher
```

Or from the repository root, using the SDK's pinned toolchain image:

```sh
docker run --rm --mount "type=bind,source=$PWD,target=/src" \
  -w /src/tools/DolphinDotNet.Launcher \
  ghcr.io/extremscorner/libogc2@sha256:13bb658d3f18903816617223f5d7c6f776d3db3ea806aae67f8d35a7a69d4c85 make
```

On PowerShell use `${PWD}` and a single line, or PowerShell's line continuation.
The bootable output is `build/DolphinDotNet.Launcher.dol` plus its bootstrap
ELF/map. The receiver's own symbols are in `build/receiver.elf` / `receiver.map`.
The DOL includes a pristine receiver image, packed from loadable segments only;
debug metadata consumes no resident memory. Generated files are ignored by Git.

Default networking uses DHCP, with TCP upload port **4299**. Static builds can
use `make clean` followed by:

```sh
make PORT=4299 IP=192.168.1.80 NETMASK=255.255.255.0 GATEWAY=192.168.1.1
```

The upload port must differ from the application's GDB port (commonly 2166 or
2828). Configure those ports separately in the debugger. USB uses the selected
virtual serial device and existing baud compatibility setting.

## Return to the receiver

The launcher remains resident across application runs. When a DolphinDotNet
application returns from native `main` or calls `exit`, libogc2 shuts down its
devices and threads, then jumps to our reload stub. The receiver restarts with
fresh runtime, network and USB state and accepts the next readiness check and
upload. Boot the launcher once, then use the same IDE run configuration again.
No new plugin setting, protocol version or external host process is required.

This uses libogc2's existing `STUBHAXX` reload convention at `0x80001800` and
its boot-time arena ceiling, as implemented in the
[platform runtime](https://github.com/extremscorner/libogc2/blob/master/libogc/system.c).
Existing SDK executables using the normal GameCube CRT need no new return hook.
Builds with sample controls intentionally keep running after managed `Main`;
press START to exit and return. Detaching the debugger leaves the program
running; return happens when the program actually exits. A crash, infinite loop,
forced hardware reset or power loss does not provide a normal exit path.

The top **2 MiB of MEM1 (`0x81600000..0x81800000`)** holds an immutable copy of
the receiver's initialized load bytes, its restore plan and a stackless restore
trampoline. A tiny bootstrap copies them there before the first receiver CRT
invocation. The low-memory reload stub survives below executable load ranges.
On exit it reapplies the arena ceiling, restores original code **and initialized
data**, zeros BSS and enters the receiver's original CRT. It never resumes the
old receiver stack, libc state, sockets or threads. The preserved image can be
restored repeatedly; it is not the receiver's working heap or upload buffer.

Applications must honor libogc2's boot-time arena ceiling and leave the reload
stub and reserved region intact. Custom CRTs, explicit arena overrides, raw
MEM1 writes or another chainloader can violate that contract and are unsupported.
The ELF validator rejects load segments (including BSS) touching resident memory;
it cannot constrain later arbitrary application writes. To leave the resident
development loop, reboot or power-cycle the console.

## Executable and memory limits

Only fixed-address **ELF32, big-endian, PowerPC ET_EXEC** is supported. Up to 16
nonoverlapping `PT_LOAD` segments are copied and each `p_memsz - p_filesz` tail
is zeroed. The entry must be aligned and inside file-backed executable bytes.
Dynamic linking/relocation is unsupported. Section tables and debug sections
are not executed or interpreted.

The file limit is **16 MiB minus 4096 bytes**, advertised during readiness. A
16 MiB high-MEM1 arena is reserved before video/network allocations; each image,
its load plan and stackless trampoline are placed at the top of that arena.
Destinations must stay inside cached MEM1 below the resident reservation,
`0x80003100..0x81600000`, and must not overwrite the actual staging allocation.
The staging arena is also below resident memory. This is a deliberate minimal
in-memory loader, without ARAM staging: a valid ELF whose load ranges collide
with staging is rejected. File size alone does not establish that it fits.

The receiver validates header/program-header bounds, integer overflows,
architecture, entry, alignment, file/memory sizes, physical/virtual addresses,
overlaps and CRC32 before acknowledging acceptance. It never executes rejected,
truncated, cancelled or unacknowledged transfers. After an explicit execute
request it acknowledges the handoff, shuts down devices and threads, disables
interrupts, then uses an original position-independent PowerPC trampoline to
copy/zero segments, flush D-cache, invalidate I-cache and branch to the new CRT.
The trampoline uses no old stack, libc, TOC/SDA or external relocation.

No command-line arguments are implemented. CRC32 and the session nonce detect corruption and
stale replies; they are not authentication. Run it on a trusted development LAN.

## Readiness and protocol

This is **DNDL v1**, distinct from Swiss wiiload. Every control frame is 16 bytes:

| Field | Encoding |
|---|---|
| magic | ASCII `DNDL` |
| version | big-endian u16, `1` |
| kind | big-endian u16 |
| value | big-endian u32 |
| detail | big-endian u32 |

| Kind | Value | Detail |
|---|---|---|
| 1 HELLO | random host nonce | 0 |
| 2 READY | echoed nonce | maximum ELF file bytes |
| 3 UPLOAD | file bytes | IEEE CRC32 of the entire ELF |
| 4 ACCEPTED | received file bytes | verified CRC32 |
| 5 EXECUTE | nonce | 0 |
| 6 EXECUTING | nonce | 0 |
| 7 ERROR | error code | 0 |
| 8 CANCEL | nonce | 0 |
| 9 COMMIT | nonce | 0 |

After READY the host may CANCEL a readiness-only check, or send UPLOAD followed
by exactly the declared number of uncompressed ELF bytes. Only after ACCEPTED
may it request EXECUTE. CANCEL is also accepted before EXECUTE. Error codes:
1 invalid request/state, 2 size, 3 checksum, 4 ELF/staging validation, 5 memory.
No upload or execute operation is automatically replayed. TCP EOF aborts the
session. A native session has a 60-second total deadline; USB cannot report host
EOF, so an abandoned partial serial upload may need a launcher restart or the
deadline to expire before a new handshake.

After EXECUTING the host sends COMMIT, proving it received the acknowledgement
before the receiver tears down networking. Until COMMIT arrives the receiver
does not execute, and CANCEL is still accepted. No final console reply is sent
after COMMIT, so the NIC cannot shut down with a required reply still queued.

Readiness means the correct receiver responded to the current configuration;
it does not prove that a particular ELF will fit or that its GDB stub is built
correctly. ACCEPTED verifies the ELF; EXECUTING acknowledges intent to hand off.
The subsequent GDB connection confirms the application's debugger is responding.

## Verification

```sh
cd tools/DolphinDotNet.Launcher
make -f tests/Makefile test 'HOST_CFLAGS=-std=c11 -O1 -g -Wall -Wextra -Werror -fsanitize=address,undefined'
python3 tests/wire_test.py
python3 tests/wire_test.py --elf build/receiver.elf
# Optional CPU tests (test-only dependency; also run in CI):
python3 -m pip install unicorn==2.1.4
python3 tests/resident_cpu_test.py
```

The native parser/state machine runs under ASan/UBSan, including resident-range
and BSS overlap rejection. Portable packer tests verify bounds and original load
bytes. The independent Python wire vectors exercise the production C core,
including the real linked receiver ELF.
`build/stdio_receiver` is a host-only test peer: it validates and reports a plan
and never executes uploaded code. Debugger tests can use it by setting
`DND_NATIVE_LAUNCHER_COMMAND` to a JSON command array (including an optional
`docker run -i` command) before running `test_console_launcher.py`.

The CPU test executes the linked PowerPC bootstrap and three subsequent returns
with an unusable old stack/SDA, overwriting the entire old receiver/bootstrap
between runs. It verifies original code/data, zero BSS, immutable resident bytes,
reload signature, disabled interrupts and arena ceilings. It stops at CRT entry;
Unicorn does not test GameCube peripherals or physical cache coherency.

Local validation also includes the native compile/link, a relocation-free
handoff object, host sanitizer tests, and the real .NET adapter talking to the
production C core before a fake GDB breakpoint session. Physical BBA/EXI and
complete CRT/device lifecycle acceptance remain outstanding. On hardware verify
boot, readiness, upload, startup, GDB breakpoint/resume, normal exit, receiver
readiness again, and a second upload, for each transport. Verify both `main`
return and `exit()` (including sample START), and that detaching alone does not
stop the application.
