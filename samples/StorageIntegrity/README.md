# Headless SD fragmentation hardware acceptance probe

This builds a GameCube DOL. **It has no UI** and performs no writes unless
one of the supported volumes contains an explicit
`dnd-defrag-test/arm.txt` file. Use a **spare / backed-up card**. Never test
power interruption on a card with irreplaceable data.

1. Build `samples/StorageIntegrity/StorageIntegrity.csproj` using the
   GameCube devkitPPC/libogc2 toolchain.
2. Create `/dnd-defrag-test/arm.txt` with text `BENCH` on the SD2SP2 card,
   or the GC Loader SD if the firmware exposes a writable volume. Do not arm
   both simultaneously; SD2SP2 is checked first.
3. Boot the DOL. Read `/dnd-defrag-test/report.txt` for the 1 MiB
   benchmark (known-length writes with opt-in off/on, ordinary streaming,
   and opt-in with 16 KiB and 64 KiB chunks).
   Times are in elapsed milliseconds; verify must be `True`. The report
   includes physical extent counts, manual repair outcomes, and automatic
   optimization counters for each benchmark.
4. Test FAT32 and exFAT media and representative cluster sizes. Repeat
   on a nearly full card with fragmented free space. Capture failures and
   relevant speed differences. On GC Loader, report firmware revision and
   whether the filesystem is writable.
5. For an interruption rehearsal put `POWER` in `arm.txt`. Reboot the
   DOL and manually remove power during repeated **create-new journaled**
   writes. On reboot the native mount recovery must remove abandoned stage
   files. The next run checks any published `inflight.bin` byte-for-byte.
   Unmount and inspect the card on a PC afterward; verify unrelated files.
   **Do not use software reset alone as a substitute for true power removal.**
6. Repeat after interrupting write and rename-related stages with a
   hardware-supported power-switch fixture if available. The exact
   checkpoint cannot be selected from this sample; use the native
   fault-injection tests for deterministic checkpoint coverage.

The harness intentionally does not mutate arbitrary files or FAT allocation
metadata. It does not certify atomicity, power-loss durability, GC Loader
firmware compatibility, creation timestamp preservation, or wear behavior;
record those separately on the actual hardware.

The managed automatic-defrag default remains disabled. The harness enables
it explicitly for benchmark and power cycles, and disables it after BENCH.
