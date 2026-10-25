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
            states.append(benchmark_telemetry(rgb))
        except AssertionError:
            try:
                states.append(telemetry(rgb))
            except AssertionError:
                continue
    return states


def validate_benchmark(raw):
    if len(raw) % FRAME_BYTES:
        raise ValueError("Incomplete benchmark RGB frame")
    phase, stress_base, bench_base = 0, None, None
    prefix = suffix = 0
    samples, cases, groups_with_detail = [], {}, set()
    previous_mask = 0
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
        state.update(timing_telemetry(rgb))
        case = state["case"]
        if state["timingCase"] != case or state["tailSamples"] > state["timingSamples"]:
            raise AssertionError("Invalid timing case/sample counts")
        # Quantization truncates each stage independently; allow <= 0.5 ms total.
        accounted = sum(state[k] for k in ("policyTenthsMs", "uploadTenthsMs", "submitTenthsMs", "endTenthsMs", "gapTenthsMs", "otherTenthsMs"))
        if not 0 <= state["worstFrameTenthsMs"] - accounted <= 6:
            raise AssertionError("Worst-frame stages do not account for the frame interval")
        if state["gpuWaitTenthsMs"] + state["retraceWaitTenthsMs"] > state["endTenthsMs"] + 1:
            raise AssertionError("GPU/retrace waits must be subsets of EndFrame")
        if not 0 <= case < 9 or state["group"] != (1, 2, 4)[case % 3] or state["budgetKiB"] != (192, 256, 384)[case // 3]:
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
    if set(cases) != set(range(9)) or previous_mask != 511 or groups_with_detail != {1, 2, 4}:
        raise AssertionError(f"Missing completed benchmark cases: {sorted(set(range(9)) - set(cases))}; "
                             f"completed mask={previous_mask}, rendered groups={sorted(groups_with_detail)}")
    for case, state in cases.items():
        if state["timingSamples"] < 3 or state["worstFrameDraws"] == 0 or state["promotions"] < 3 or state["measuredMs"] < 6000 or state["frameP99TenthsMs"] == 0:
            raise AssertionError("Insufficient real measurements for case " + str(case))
    # Three quarter-screen atlases cannot exceed 75% at the initial aligned pose.
    if cases[2]["staticCoverageBasisPoints"] > 7510:
        raise AssertionError("192 KiB atlas static coverage is overstated")
    return dict(benchmark=True, completedCases=[cases[i] for i in range(9)],
                sampledFrames=len(samples), stressFramesBefore=prefix, stressFramesAfter=suffix,
                renderedGroups=sorted(groups_with_detail), stressNativeBaselineKiB=stress_base,
                benchmarkNativeBaselineKiB=bench_base, samples=samples)
