# CI performance proposals

The options below record the original proposals. The first implementation keeps
all required checks and changes their execution strategy as described below.

## Implemented first batch

- Five managed groups run compiler, I/O, async/network, audio/games and
  graphics/models in parallel. All 41 original managed contracts remain.
- The native emulator-artifact job builds only the artifacts and reports needed
  by Dolphin. Two concurrent groups retain the other PowerPC/transport and sample
  checks. The final CI gate requires all three, while Dolphin depends only on its
  artifact producer. All 27 native contracts remain.
- Four isolated Dolphin groups run games, debugger, runtime and controllers.
  The final gate rejects failed, skipped or missing selected cases. All 13
  emulator scenarios remain, including all 285 controller cases and the negative
  control. Artifact/log names include the group to prevent collisions.
- Debugger/marker scenarios terminate after all probe and log evidence is ready.
  Pong and Snake retain their existing 65/70-second gameplay soak. Deadlines,
  abnormal exits, PPC diagnostics, object inspection and telemetry readback remain
  checked; the complete emulator process group is cleaned up.
- Each managed/native group builds a shared Release compiler and invokes its
  DLL directly. Compiler integration and the compile-sample script support this
  mode while standalone use retains project-based execution. Compiler integration
  no longer performs a duplicate native HelloGameCube build; SDK/native coverage
  remains in its dedicated jobs.
- CI splits multi-source GCC commands into private objects, uses ccache for each
  compile and then preserves the original link order/options. Headers, flags,
  architecture and compiler identity remain ccache inputs. Cache contents are
  never reused merely because an output file exists. Native Make builds also use
  ccache and two compile workers.
- Native bundles share the pinned EXI source checkout within a job. Existing
  pinned commits need no repeated fetch, and source verification still runs for
  every rebuilt archive. No SDK archive or verified-source check is bypassed.

`tests/ci_coverage_manifest.json` and workflow regression tests protect the
pre-sharding contracts and the aggregate result handling. Local real-Dolphin
checks cover early guest entry and DAP object/string inspection with disconnect
telemetry readback. The full CI run will provide cold-cache timings; the original
22-minute baseline below is retained for comparison. OS matrices, stress/ABI
coverage, whole-PR selection and merge requirements have not been reduced.

## Measured baseline

Completed run [39060188226](https://github.com/capz/DolphinDotNet/actions/runs/39060188226),
before the controller acceptance gate, spent approximately:

| Job | Wall time |
| --- | ---: |
| Managed compiler | 18m 30s |
| GameCube DOL | 10m 42s |
| Dolphin headless | 10m 56s, after GameCube DOL |
| Linux IDE workflow | 5m 18s |
| Rider plugin and verifier | 3m 17s |

The GameCube-to-Dolphin dependency chain therefore takes about 21m 38s,
excluding queue/setup gaps. Optimizing only the managed job will not fix that
critical path. Timings are from job-log timestamps; command-group intervals
include all commands within that step.

The workflow contains 127 explicit dotnet build/run invocations. The Linux IDE
job spends about four minutes building every sample with Docker. Several Dolphin
checks run for their full 40–70-second timeout even after proving their required
guest execution. The existing selector uses the whole PR diff, so a broad PR
continues selecting every touched subsystem on later small pushes. Superseded-run
cancellation and Gradle caching already exist.

## Distinct options

| # | Proposal | Expected benefit | Coverage / cost |
| --- | --- | --- | --- |
| 1 | End Dolphin processes when all required assertions complete, retaining a hard deadline and guaranteed cleanup. Rendering/audio tests must first collect their required frame/audio evidence. | Very high: removes fixed idle time from the longest chain. | Keeps assertions; moderate harness work. |
| 2 | Split the managed job into compiler/runtime, storage/BCL, audio/model/graphics and network/async shards. Balance using measured durations. | Very high: replaces an 18-minute serial queue with shorter concurrent jobs. | Keeps tests; uses more runners and setup. |
| 3 | Build and upload emulator-required DOL/ELF/map artifacts in an early dedicated job. Let remaining PowerPC ABI/sample checks run alongside emulation. | High: removes the wait for unrelated native checks before Dolphin starts. | Keeps checks; artifact dependency refactor. |
| 4 | Run independent Dolphin scenarios in a small matrix, with isolated user directories, pipes, logs and ports. | High: reduces remaining serial emulator time. | Keeps tests; cap concurrency to avoid CPU/RAM contention. |
| 5 | Compile common native runtime objects once per flags/ABI/toolchain variant and link each generated fixture against them; add ccache for host GCC and PowerPC. | High where native compilation repeats. | Keys must include headers, flags, source, ABI and pinned SDK/EXI identity. |
| 6 | Restore/build the compiler, adapter and common managed projects once per job; invoke built DLLs or use --no-build/--no-restore after graph restoration. | Medium/high: avoids repeated MSBuild startup and dependency checks. | Keep explicit restore/build integration tests for those behaviors. |
| 7 | Cache NuGet packages and verified, pinned EXI sources/archives and toolchain downloads. Improve existing Gradle caching only where logs show misses. | Medium, especially cold runs. | Cache downloads/immutable inputs; validate identities and never bypass verification. |
| 8 | Maintain pinned CI images with Dolphin/Xvfb, .NET, multilib, ffmpeg and assimp already installed for the jobs that use them. | Medium: removes repeated apt/setup delays and variability. | Image maintenance and scheduled refreshes. |
| 9 | Refine the test dependency map below the current broad native/managed groups. Input changes select input/runtime/ABI/emulator tests; launcher changes select Windows/macOS/Linux workflows. | High for focused changes. | Compiler/GC/shared ABI changes stay broad; unknown files still select everything. |
| 10 | Reuse successful component checks by complete input fingerprint, including the base revision and all transitive source/build/test dependencies. Keep whole-PR accounting. | High for later small updates to a large PR. | More infrastructure; never replace the PR diff with HEAD~1 and silently forget untested changes. |
| 11 | Use fast required PR checks, expanded ready-for-review checks, and a mandatory full merge-candidate check; run exhaustive sweeps nightly/on demand. | Large reduction in iteration latency. | Some failures move later; full coverage must complete before merge, including base changes. |
| 12 | Scope the OS matrix to relevant changes. Always run Linux logic tests; retain Windows/macOS checks for launchers, SDK, paths, packaging and public build behavior. | Moderate compute/setup savings. | Cross-platform surprises can be found later; scheduled full matrix remains necessary. |
| 13 | Scope expensive execution variants: keep relevant 32-bit ABI tests and targeted sanitizers/GC stress on PRs; run every redundant combination in the full suite. | Medium on managed checks. | Do not defer 32-bit/PPC checks for layout, pointer, GC or compiler changes. |
| 14 | Assign each end-to-end contract an owner and remove duplicate builds/setup across IDE, native and emulator jobs. Keep separate raw-RSP, DAP and packaged-Java assertions where they cover different layers. | Medium. | Audit evidence before calling tests duplicates; share artifacts only when their identities match. |
| 15 | Run cheap compiler/native/adapter regressions as an early gate for expensive downstream tests. | Faster failures and less wasted compute. | Does not necessarily improve successful-run wall time; reuse the gate's build products. |
| 16 | Use larger/faster runners for the remaining CPU-bound native/compiler shards. | Medium, after eliminating idle waits. | Higher cost; little benefit for fixed sleeps or network waits. |
| 17 | Keep future PRs focused by subsystem, so path selection can exclude unrelated work. | High selection benefit for future work. | Workflow/process change; a broad compiler/runtime change still needs broad coverage. |

## Recommended order

Start with 1, 2, 3, 5 and 6: these preserve the test assertions while addressing
both long paths. Then add 4, 7 and 8 based on fresh timing data. Improve dependency
selection with 9, and consider 10/11 only with explicit proof/revalidation rules.

An initial target is under ten minutes for the same relevant checks, followed by
a 3–5-minute fast feedback lane. These are engineering targets to validate after
measurement, not promised timings or additive per-option savings. Keep the full
285 controller cases for input changes: their measured local run is about 76
seconds, and they already found real bugs. Preserve test-count, fingerprint,
skip-reason and timing reports so a shorter green run cannot mean lost coverage.
