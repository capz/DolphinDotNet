# TextureStrategy frame-stage measurements

Use the `texture-hardware-Release` artifact from the TextureStrategy in Dolphin workflow. This interactive build keeps results on screen; the automated proof build returns to stress mode after completion. Run the same DOL as before; START switches stress/comparison modes. A reruns all nine cases. L switches between coverage and stage timings. R shows the nine-case table; LEFT/RIGHT selects a case and opens its detail view. Existing tests, coverage metrics and budgets are retained.

The timing table shows each case's **worst measured frame** with stage costs from that same frame. The detail view reports per-stage mean, mean among frames over 33.333 ms, independent maximum, and the correlated worst-frame sample. The worst frame is not the P99 sample. Repeating the suite helps distinguish recurring costs from one-off events. Compare 2x2 at 256 and 384 KiB first.

| Stage | Measurement |
|---|---|
| POLICY | Camera/demand selection, cache search, request handling and native-byte checks, excluding texture publication |
| UPLOAD | Old texture disposal, new Texture2D allocation/conversion/publication and promotion bookkeeping |
| DRAW | BeginFrame through End3D, including coarse and detailed submissions |
| END | Whole managed EndFrame, including native overlay, display copy, GPU completion, retrace, collection and executor handoff |
| GPU | Blocking GX_DrawDone after display copy; outstanding work only, not total GPU execution time |
| VI | VIDEO_WaitVSync blocking time |
| GAP | End of the previous Present to start of the next; includes Task.Delay, controller/input, scheduling and background executor work |
| OTHER | Residual frame time: measurement/reporting, telemetry submission and remaining unscoped work |
| PREP | Overlap of the most recent completed background Fill with this measured frame interval; independent of publication |

POLICY + UPLOAD + DRAW + END + GAP + OTHER account for a frame. GPU and VI are **subsets** of END. PREP can overlap GAP and is not additive; queue delay is not separately counted. Preparation is attributed by execution timestamps, including completed requests later discarded; it is not shifted to the publication frame. These are wall times, so scheduler interruption can appear in any scope. This is still a procedural source benchmark, not a storage latency test.

Capture the L/R timing table and the selected-case detail view on hardware. A higher UPLOAD tail points at publication cost; DRAW at submission work; GPU at outstanding graphics work; VI at pacing; GAP/PREP at preparation/scheduling. These are diagnostic correlations, not causal proof. The measurement overlay itself adds work, mostly OTHER/submission and GPU time; use the same instrumented binary for comparisons.

The original 32-field strip at y=448 is unchanged. Comparison frames add a 16-field strip at y=432, magic 0x4254/version 1. Timings use tenths of a millisecond and saturate at 65535. The automated Dolphin checker requires stage samples, checks same-frame accounting and nested wait bounds, and exports all timings in results.json. It preserves the original stress/comparison/stress acceptance checks.
