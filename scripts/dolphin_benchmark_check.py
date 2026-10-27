"""Accept a real stress -> nine-case benchmark -> stress round trip in one DOL."""
import math
from dolphin_streaming_check import FRAME_BYTES, telemetry, rendered_world

FIELDS = ("magic", "version", "frame", "case", "cameraX256", "cameraY256",
          "residentKiB", "promotions", "evictions", "coverageBasisPoints", "group", "budgetKiB",
          "elapsedMs", "completedMask", "measuredMs", "staticCoverageBasisPoints",
          "requestedKiB", "uploadedKiB", "latencyP95Ms", "longestBelow95Ms", "unusedKiB",
          "nativeKiB", "cancelled", "draws", "maxUploadUs", "frameP95TenthsMs",
          "frameP99TenthsMs", "maxFrameTenthsMs", "presentTenthsMs", "motionCoverageBasisPoints",
          "cold95Ms", "peakResidentKiB")


def benchmark_telemetry(rgb):
    return dict(zip(FIELDS, telemetry(rgb, 0x424d, 1).values()))


TIMING_FIELDS = ("timingMagic", "timingVersion", "timingCase", "timingSamples", "tailSamples",
                 "worstFrameTenthsMs", "policyTenthsMs", "uploadTenthsMs", "submitTenthsMs",
                 "endTenthsMs", "gpuWaitTenthsMs", "retraceWaitTenthsMs", "gapTenthsMs",
                 "otherTenthsMs", "preparationTenthsMs", "worstFrameDraws")


PIPELINE_FIELDS = ("pipelineMagic", "pipelineVersion", "pipelineCase", "optimizationMode", "preparationDepth", "publicationCap", "started", "published", "reused", "maxPublications", "preparationMeanTenthsMs", "preparationMaxTenthsMs", "readyMeanTenthsMs", "scratchKiB", "beginCopyMeanTenthsMs", "gateMeanTenthsMs")

MEAN_FIELDS=("meanMagic","meanVersion","meanCase","meanPolicyTenthsMs","meanUploadTenthsMs","meanSubmitTenthsMs","meanEndTenthsMs","meanGpuTenthsMs","meanViTenthsMs","meanGapTenthsMs","meanOtherTenthsMs","queueMeanTenthsMs","beginCopies","allocations","pendingPeak","readyPeak")

P99_FIELDS=("p99Magic","p99Version","p99Case","p99Samples","p99RankIndex","p99ExactTenthsMs","p99PolicyTenthsMs","p99UploadTenthsMs","p99SubmitTenthsMs","p99EndTenthsMs","p99GpuTenthsMs","p99ViTenthsMs","p99GapTenthsMs","p99OtherTenthsMs","p99OverlapTenthsMs","p99Draws")

def timing_telemetry(rgb):
    values = []
    for field in range(16):
        value = 0
        for bit in range(16):
            index = field * 16 + bit
            at = ((434 + index // 64 * 4) * 640 + index % 64 * 10 + 5) * 3
            pixel = rgb[at:at + 3]
            if max(pixel) - min(pixel) > 12:
                raise AssertionError("Corrupt timing strip")
            if sum(pixel) > 384:
                value |= 1 << bit
        values.append(value)
    if values[0] == 0x4252 and values[1] == 1:
        return dict(zip(P99_FIELDS, values))
    if values[0] == 0x4251 and values[1] == 1:
        return dict(zip(MEAN_FIELDS, values))
    if values[0] == 0x4250 and values[1] in (1,2,3):
        return dict(zip(PIPELINE_FIELDS, values))
    state = dict(zip(TIMING_FIELDS, values))
    if state["timingMagic"] != 0x4254 or state["timingVersion"] != 1:
        raise AssertionError("Missing stage timings")
    return state


def coverage_ceiling(state):
    """Independent geometric ceiling: largest visible intersections that fit."""
    cx, cy = state["cameraX256"] / 256, state["cameraY256"] / 256
    hy = 5.5 * math.tan(math.radians(27.5))
    hx, group = hy * 4 / 3, state["group"]
    areas = []
    for y in range(0, 32, group):
        for x in range(0, 48, group):
            w = max(0, min(x + group, cx + hx) - max(x, cx - hx))
            h = max(0, min(y + group, cy + hy) - max(y, cy - hy))
            areas.append(w * h)
    slots = state["budgetKiB"] // (group * group * 4)
    return min(10000, round(sum(sorted(areas, reverse=True)[:slots]) / (4 * hx * hy) * 10000))


def capture_states(raw):
    states = []
    for offset in range(0, len(raw), FRAME_BYTES):
        rgb = raw[offset:offset + FRAME_BYTES]
        try:
            state=benchmark_telemetry(rgb)
            try:
                state.update(timing_telemetry(rgb))
            except AssertionError as error:
                state["stripError"]=str(error)
            states.append(state)
        except AssertionError:
            try:
                states.append(telemetry(rgb))
            except AssertionError:
                continue
    return states


def validate_benchmark(raw, pipeline=False, matrix_budget=0, glyph_matrix=False, rectangle_matrix=False):
    if len(raw) % FRAME_BYTES:
        raise ValueError("Incomplete benchmark RGB frame")
    phase, stress_base, bench_base = 0, None, None
    prefix = suffix = 0
    samples, cases, groups_with_detail = [], {}, set()
    previous_mask = 0
    pipeline_cases, timing_cases, mean_cases, p99_cases = {}, {}, {}, {}
    for offset in range(0, len(raw), FRAME_BYTES):
        rgb = raw[offset:offset + FRAME_BYTES]
        try:
            state = benchmark_telemetry(rgb)
            is_benchmark = True
        except AssertionError:
            try:
                state = telemetry(rgb)
                is_benchmark = False
            except AssertionError:
                if phase == 0 and prefix == 0:
                    continue
                raise AssertionError("Missing telemetry during comparison round trip")
        if not is_benchmark:
            if phase == 1:
                phase = 2
            if phase == 0:
                prefix += 1
            else:
                suffix += 1
            baseline = state["nativeKiB"] - state["residentKiB"]
            if stress_base is None:
                stress_base = baseline
            if baseline != stress_base or state["residentKiB"] > 192:
                raise AssertionError("Resources leaked when returning to original stress mode")
            rendered_world(rgb, state)
            continue
        if phase == 2:
            raise AssertionError("Unexpected benchmark restart")
        phase = 1
        strip = timing_telemetry(rgb)
        case = state["case"]
        complete = bool(state["completedMask"] & (1 << case))
        if "p99Magic" in strip:
            if not complete or strip["p99Case"]!=case:
                raise AssertionError("P99 diagnostics require completed matching case")
            p99_cases[case]=strip
        elif "meanMagic" in strip:
            if not complete or strip["meanCase"]!=case:
                raise AssertionError("Mean diagnostics require completed matching case")
            mean_cases[case]=strip
        elif "pipelineMagic" in strip:
            if not complete or strip["pipelineCase"] != case:
                raise AssertionError("Pipeline diagnostics require completed matching case")
            pipeline_cases[case] = strip
        else:
            state.update(strip)
            if state["timingCase"] != case or state["tailSamples"] > state["timingSamples"]:
                raise AssertionError("Invalid timing case/sample counts")
            accounted = sum(state[k] for k in ("policyTenthsMs", "uploadTenthsMs", "submitTenthsMs", "endTenthsMs", "gapTenthsMs", "otherTenthsMs"))
            if not 0 <= state["worstFrameTenthsMs"] - accounted <= 6:
                raise AssertionError("Worst-frame stages do not account for the frame interval")
            if state["gpuWaitTenthsMs"] + state["retraceWaitTenthsMs"] > state["endTenthsMs"] + 1:
                raise AssertionError("GPU/retrace waits must be subsets of EndFrame")
            if complete:
                timing_cases[case] = strip
        if not 0 <= case < 9 or state["group"] != (2 if matrix_budget else (1, 2, 4)[case % 3]) or state["budgetKiB"] != (matrix_budget or (192, 256, 384)[case // 3]):
            raise AssertionError("Wrong comparison layout/budget")
        if state["residentKiB"] > state["budgetKiB"] or state["peakResidentKiB"] > state["budgetKiB"]:
            raise AssertionError("Comparison cache exceeded native byte budget")
        baseline = state["nativeKiB"] - state["residentKiB"]
        if bench_base is None:
            bench_base = baseline
        if baseline != bench_base:
            raise AssertionError("Comparison resources leaked between cases")
        if not 0 <= state["coverageBasisPoints"] <= coverage_ceiling(state) + 12:
            raise AssertionError("Reported coverage exceeds independent allocation ceiling")
        if state["uploadedKiB"] > state["requestedKiB"] or state["unusedKiB"] > state["residentKiB"]:
            raise AssertionError("Invalid comparison transfer/usefulness accounting")
        mask = state["completedMask"]
        if mask | previous_mask != mask:
            raise AssertionError("Completed comparison results disappeared")
        previous_mask = mask
        coarse, detail = rendered_world(rgb, state)
        if state["coverageBasisPoints"] >= 9950 and coarse > 1:
            raise AssertionError("Full-detail report disagrees with rendered coarse pixels")
        if detail:
            groups_with_detail.add(state["group"])
        if state["frameP95TenthsMs"] > state["frameP99TenthsMs"]:
            raise AssertionError("Invalid frame-time percentiles")
        if mask & (1 << case):
            cases[case] = state
        samples.append(state)
    if prefix < 5 or suffix < 5:
        raise AssertionError("The same DOL must run original stress mode before AND after comparison")
    if set(cases) != set(range(9)) or previous_mask != 511 or groups_with_detail != ({2} if matrix_budget else {1, 2, 4}):
        raise AssertionError(f"Missing completed benchmark cases: {sorted(set(range(9)) - set(cases))}; "
                             f"completed mask={previous_mask}, rendered groups={sorted(groups_with_detail)}")
    if pipeline and set(pipeline_cases) != set(range(9)):
        raise AssertionError("Missing completed pipeline diagnostics")
    for case, state in cases.items():
        if case not in timing_cases:
            raise AssertionError("Missing completed stage timings")
        state.update(timing_cases[case])
        if case in pipeline_cases:
            state.update(pipeline_cases[case])
            if matrix_budget and state["optimizationMode"] != ((0,31,32,33,34,35,36,37,34) if rectangle_matrix else (0,19,25,18,26,27,28,29,26) if glyph_matrix else (0,14,2,15,16,17,7,9,11))[case]:
                raise AssertionError("Wrong pipeline matrix mode")
            if state["published"] != state["promotions"] or not 1 <= state["maxPublications"] <= state["publicationCap"]:
                raise AssertionError("Invalid publication accounting")
            if state["scratchKiB"] > 128 or state["preparationDepth"] > 5:
                raise AssertionError("Pipeline exceeded bounded scratch policy")
        if state.get("pipelineVersion",0)>=2:
            if case not in mean_cases:raise AssertionError("Missing frozen mean diagnostics")
            state.update(mean_cases[case])
            if state["pendingPeak"]>state["preparationDepth"] or state["readyPeak"]>state["preparationDepth"]:
                raise AssertionError("Unbounded pipeline queue")
            clear=state["optimizationMode"] in (2,7,8,9,10,11,15,16,17,18,26,27,28,29,33,34,35,36,37)
            if clear and state["beginCopies"]>1:
                raise AssertionError("Same-color completed clear reuse was not exercised")
            if not clear and state["beginCopies"]<state["timingSamples"]-1:
                raise AssertionError("Clear control skipped copies")
            if state["reused"]+state["allocations"]!=state["promotions"]:
                raise AssertionError("Invalid allocation/reuse accounting")
        if state.get("pipelineVersion")==3:
            if case not in p99_cases:raise AssertionError("Missing correlated P99 diagnostics")
            state.update(p99_cases[case])
            n=state["timingSamples"]
            if state["p99Samples"]!=n or state["p99RankIndex"]!=n-(n*99+99)//100:
                raise AssertionError("Wrong correlated P99 rank")
            delta=state["frameP99TenthsMs"]-state["p99ExactTenthsMs"]
            if not 0<=delta<=3:raise AssertionError("P99 snapshot disagrees with frame histogram")
            if state["p99GpuTenthsMs"]+state["p99ViTenthsMs"]>state["p99EndTenthsMs"]+1:
                raise AssertionError("P99 waits are not subsets of EndFrame")
            accounted=sum(state[k] for k in ("p99PolicyTenthsMs","p99UploadTenthsMs","p99SubmitTenthsMs","p99EndTenthsMs","p99GapTenthsMs","p99OtherTenthsMs"))
            if not 0<=state["p99ExactTenthsMs"]-accounted<=6:raise AssertionError("P99 phases do not close")
        if state["timingSamples"] < 3 or state["worstFrameDraws"] == 0 or state["promotions"] < 3 or state["measuredMs"] < 6000 or state["frameP99TenthsMs"] == 0:
            raise AssertionError("Insufficient real measurements for case " + str(case))
    # Three quarter-screen atlases cannot exceed 75% at the initial aligned pose.
    if not matrix_budget and cases[2]["staticCoverageBasisPoints"] > 7510:
        raise AssertionError("192 KiB atlas static coverage is overstated")
    return dict(benchmark=True, matrixBudgetKiB=matrix_budget, completedCases=[cases[i] for i in range(9)],
                sampledFrames=len(samples), stressFramesBefore=prefix, stressFramesAfter=suffix,
                renderedGroups=sorted(groups_with_detail), stressNativeBaselineKiB=stress_base,
                benchmarkNativeBaselineKiB=bench_base, samples=samples)
