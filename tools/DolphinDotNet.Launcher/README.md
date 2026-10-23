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
The output is `build/DolphinDotNet.Launcher.dol` plus its ELF/map. The current
linked DOL is approximately 312 KiB. These generated files are ignored by Git.

Default networking uses DHCP, with TCP upload port **4299**. Static builds can
use `make clean` followed by:

```sh
make PORT=4299 IP=192.168.1.80 NETMASK=255.255.255.0 GATEWAY=192.168.1.1
```

The upload port must differ from the application's GDB port (commonly 2166 or
2828). Configure those ports separately in the debugger. USB uses the selected
virtual serial device and existing baud compatibility setting. This is a
one-shot chainload: the receiver is replaced by the uploaded program. Return to
the launcher through your boot method before the next upload.

## Executable and memory limits

Only fixed-address **ELF32, big-endian, PowerPC ET_EXEC** is supported. Up to 16
nonoverlapping `PT_LOAD` segments are copied and each `p_memsz - p_filesz` tail
is zeroed. The entry must be aligned and inside file-backed executable bytes.
Dynamic linking/relocation is unsupported. Section tables and debug sections
are not executed or interpreted.

The file limit is **16 MiB minus 4096 bytes**, advertised during readiness. A
16 MiB high-MEM1 arena is reserved before video/network allocations; each image,
its load plan and stackless trampoline are placed at the top of that arena.
Destinations must stay inside cached MEM1, `0x80003100..0x81800000`, and must
not overwrite this actual staging allocation. This is a deliberate minimal
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

No command-line arguments, persistent resident service or automatic return to
the receiver is implemented. CRC32 and the session nonce detect corruption and
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

After READY the host may CANCEL a readiness-only check, or send UPLOAD followed
by exactly the declared number of uncompressed ELF bytes. Only after ACCEPTED
may it request EXECUTE. CANCEL is also accepted before EXECUTE. Error codes:
1 invalid request/state, 2 size, 3 checksum, 4 ELF/staging validation, 5 memory.
No upload or execute operation is automatically replayed. TCP EOF aborts the
session. A native session has a 60-second total deadline; USB cannot report host
EOF, so an abandoned partial serial upload may need a launcher restart or the
deadline to expire before a new handshake.

Readiness means the correct receiver responded to the current configuration;
it does not prove that a particular ELF will fit or that its GDB stub is built
correctly. ACCEPTED verifies the ELF; EXECUTING acknowledges intent to hand off.
The subsequent GDB connection confirms the application's debugger is responding.

## Verification

```sh
cd tools/DolphinDotNet.Launcher
make -f tests/Makefile test 'HOST_CFLAGS=-std=c11 -O1 -g -Wall -Wextra -Werror -fsanitize=address,undefined'
python3 tests/wire_test.py
python3 tests/wire_test.py --elf build/DolphinDotNet.Launcher.elf
```

The native parser/state machine runs under ASan/UBSan. The independent Python
wire vectors exercise the production C core, including the real linked ELF.
`build/stdio_receiver` is a host-only test peer: it validates and reports a plan
and never executes uploaded code. Debugger tests can use it by setting
`DND_NATIVE_LAUNCHER_COMMAND` to a JSON command array (including an optional
`docker run -i` command) before running `test_console_launcher.py`.

Local validation includes the native GameCube compile/link, a relocation-free
handoff object, host sanitizer tests, and the real .NET adapter talking to the
production C core before a fake GDB breakpoint session. These are not physical
BBA/EXI or PowerPC execution acceptance. Before hardware use, verify boot,
readiness, upload, visible application startup, GDB breakpoint/resume, disconnect,
and a second upload after returning to the launcher, for each transport.
