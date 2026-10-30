# JSystem owned-game hardware acceptance harness (priority 1)

This implements the first stage of the JSystem fidelity roadmap: a reproducible,
bounded **on-device** asset compatibility corpus, automatic frame capture, and
Golden XFB assertions. It does **not** implement FLD1 or additional material
semantics from later roadmap items. It does not claim a passing parser is visually
accurate to retail GameCube rendering.

## Scope and discovery

The standalone `tests/JSystemHardwareFixture` GameCube Debug DOL mounts
`gcloader:/games` (SD via the GC Loader libogc2 backend, if supported by the
installed firmware). When it is unavailable, `sd:/games` on SD2SP2 is the
fallback for emulator and hardware development. Only **original, seekable,
plain GameCube ISO/GCM images** are eligible. RVZ, compressed ISO and Wii
images are not supported. The source mount must support opening an ISO image
as `dvd:/`; a selected ODE disc and raw GC Loader SD access may be mutually
exclusive on some firmware versions.

The DOL visits at most 512 entries across up to four levels of /games folders,\nenumerates at most 256 image filenames, opens at most four sorted
ISO/GCM images, visits at most 48 directories and 192 files per disc and samples
at most eight fixture candidates per game (24 across all games). It checks
standalone BTI, J3D BMD/BDL, BTP/BTK/BRK/BVA, JPAC, BMG and BFN assets,
plus recognized members of bounded Yaz0/RARC containers. Source archives are
at most 6 MiB and selected member buffers at most 4 MiB. File types are
detected using magic and decoder validation, **never extension alone**.
Archive members are not extracted to disk. Path/name telemetry is ASCII-safe,
truncated to 191 bytes, and no proprietary game content is committed or uploaded.

The subset is intentionally sampled. A clean run must not be construed as
exhaustive corpus coverage; games with formats inside proprietary archive
variants may expose no compatible fixtures. Asset enumeration order can vary
across filesystems and the bounded discovery window; preserve the same
firmware/image set for comparable golden tests.

### Per-case behavior

- **BTI**: decode full palette/mipmap/sampler metadata and submit a centered
  native GX texture on a controlled clear background.
- **J3D2 BMD/BDL**: parse MAT3 names and attempt native model import and a
  stationary-camera draw. Advanced TEV and original game transforms are not
  validated automatically.
- **JPAC**: identify validated version/count metadata only on the console.
  Full JPAC effect/chunk decoding is host-tested separately; the current AOT
  compiler cannot lower the full JPAC reader. No particle scene is claimed.
- **BMG, BFN, J3D1 material tracks**: decode and report metadata counts;
  they display a plain background rather than pretending to render typography
  or material/shape animation.
- **Invalid/unsupported types**: record a failure or explicit skip, do not
  execute file contents or continue guessing unsupported binary layouts.

The fixture DOL reports a 248-byte `dnd_jsystem_fixture` native status record
via the same stopped-GDB RSP access as `dnd_automation_mailbox`. Its
`name` field is diagnostic identification, not an archive extraction command.
Native schema v1 has 14 big-endian u32 fields followed by 192 name bytes:
magic `DNFI`, version, serial, phase, source index/count, asset index/count,
parsed, skipped, failed, file-format kind, element count, name length, name.
The record updates before the next frame boundary; native code commits the
serial last. No GDB extension command or debugger plugin change is required.

## Building the bootable Debug DOL

Requires .NET 8, devkitPPC/libogc2 and the verified EXI source overlay used
by DolphinDotNet's standard native bundle builds:

```sh
# USB Gecko in GameCube EXI slot A:
bash scripts/build_jsystem_hardware_fixture.sh usb 0
# Alternative BBA GDB:
bash scripts/build_jsystem_hardware_fixture.sh tcp 192.168.1.50 255.255.255.0 192.168.1.1
```

The AOT bundler runs with `--debug-symbols`, enabling both the original GDB
debugger and new fixture/automation telemetry; it produces a debug ELF and
a bootable DOL in `out/jsystem-hardware-fixture/`. Run the ELF via Swiss
USB Gecko upload or DolphinDotNet's existing launcher. Return to Swiss between
launches as needed; **do not try to run a game's ISO and the fixture DOL at the
same time.** The fixture opens files from the GC Loader filesystem as data.

## Running the host automation

The console must be running the test ELF with the GDB stub stopped at startup.
Do not simultaneously attach Rider/Visual Studio to the same GDB connection.

```sh
# USB Gecko virtual serial port; install pyserial first:
python3 scripts/jsystem_hardware_regression.py \
  --elf out/jsystem-hardware-fixture/Game.elf \
  --serial COM5 --baud 115200 --timeout 300 \
  --output out/jsystem-acceptance/

# TCP/BBA / emulated GDB:
python3 scripts/jsystem_hardware_regression.py \
  --elf out/jsystem-hardware-fixture/Game.elf \
  --tcp 127.0.0.1:2166 --timeout 300 \
  --output out/jsystem-acceptance/
```

The runner waits for the first completed GX frame via PR #141's debugger
breakpoint, reads per-fixture telemetry, stores `case-XX.png` and raw-XFB
SHA-256 entries in `results.json`, then injects a neutral controller state
followed by a fresh **A** press for each next case. This handles
connect-baseline suppression correctly. A GameCube frame involves one
`Controller.Update()`; the input-poll and frame counters are checked.
The host never asks the DOL to write a file or leak raw asset bytes.

To establish an owned-disc hardware golden, archive an accepted
`results.json` outside the repository, then rerun with
`--golden path/to/accepted-results.json`. Missing cases, changed native
frame hashes, and parser failures cause a nonzero test result. Baselines
without `--golden` merely **record** current output and are not proof of
retail GX parity; for actual accuracy assess screenshots against original
game/Dolphin reference captures separately. USB Gecko full-frame transfer
uses many 512-byte RSP reads and may be slow.

No `gcloader:/games` directory, unsupported GC Loader block access or zero
detected fixtures is a test configuration error, not a green result.
Unsupported file profiles are recorded separately from malformed decodable
files. The fixture modifies no retail files.

## Verification and open limits

```sh
dotnet run --project tests/JSystemFixtureHostTests
python3 -m unittest discover -s tests -p test_jsystem_hardware_regression.py
make -C tests jsystem_fixture_telemetry_test && tests/jsystem_fixture_telemetry_test
python3 scripts/ci_test_suite.py gamecube
```

CI builds and AOT-compiles the fixture, and runs synthetic parser/telemetry
and host orchestration tests. **Physical GC Loader SD sector access, native
frame captures, USB Gecko reliability, material/particle parity and real
sample selection need actual console/disc acceptance.** Capture and compare
owned fixtures before claiming any retail-compatible visual result.
