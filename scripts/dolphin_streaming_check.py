"""Validate guest-written streaming counters AND the corresponding rendered world."""
import math

FRAME_BYTES = 640 * 480 * 3
class RenderMismatch(AssertionError):
    def __init__(self, message, rgb, state):
        super().__init__(message)
        self.rgb, self.state = rgb, state

FIELDS = ("magic", "version", "frame", "mode", "cameraX256", "cameraY256",
          "residentKiB", "promotions", "evictions", "fallbackCells", "detailCells",
          "failures", "cancelled", "switches", "reversals", "peakResidentKiB",
          "requestedKiB", "uploadedKiB", "maxLatencyFrames", "budgetWaitFrames",
          "visibleCells", "nativeKiB", "pendingFrames", "draws", "uploadUs",
          "p95LatencyFrames", "p99LatencyFrames", "maxUploadUs", "frameTenthsMs",
          "sourceBusy", "modeDisposals", "coarseKiB")


def telemetry(rgb):
    if len(rgb) != FRAME_BYTES:
        raise ValueError("Incomplete streaming RGB frame")
    values = []
    for field in range(len(FIELDS)):
        value = 0
        for bit in range(16):
            index = field * 16 + bit
            at = ((450 + (index // 64) * 4) * 640 + (index % 64) * 10 + 5) * 3
            pixel = rgb[at:at + 3]
            if max(pixel) - min(pixel) > 12:
                raise AssertionError("Corrupt telemetry strip")
            if sum(pixel) > 3 * 128:
                value |= 1 << bit
        values.append(value)
    result = dict(zip(FIELDS, values))
    if result["magic"] != 0x5453 or result["version"] != 3:
        raise AssertionError("No streaming telemetry: blank, stale or error screen")
    return result


def rendered_world(rgb, state):
    cx, cy = state["cameraX256"] / 256, state["cameraY256"] / 256
    scale = 240 / (5.5 * math.tan(math.radians(55 / 2)))
    coarse = detailed = total = 0
    for dy in range(-1, 2):
        for dx in range(-2, 3):
            x, y = int(cx) + dx, int(cy) + dy
            px = round(320 + (x + .375 - cx) * scale)
            py = round(240 - (y + .375 - cy) * scale)
            if not (20 <= px < 620 and 150 <= py < 420):
                continue
            total += 1
            at = (py * 640 + px) * 3
            actual = rgb[at:at + 3]
            lo = (18 + (x % 12) * 8, 26 + (y % 12) * 8, 84)
            hi = (45 + (x % 12) * 12, 65 + (y % 12) * 11, 184)
            expected = hi if (x * 3 + y) & 1 else lo
            fallback = ((lo[0] + hi[0]) // 2, (lo[1] + hi[1]) // 2, 134)
            distance = lambda color: max(abs(a - b) for a, b in zip(actual, color))
            if distance(expected) <= 18:
                detailed += 1
            elif distance(fallback) <= 18:
                coarse += 1
    if total < 8 or coarse + detailed < total - 1:
        raise RenderMismatch(f"World holes/wrong pixels at camera {cx},{cy}: {coarse + detailed}/{total} cells", rgb, state)
    return coarse, detailed


def validate_stream(raw, start_mode):
    if len(raw) % FRAME_BYTES:
        raise ValueError("Incomplete decoded streaming frame")
    states = []
    modes, detail_modes, fallback_modes, positions = set(), set(), set(), set()
    native_base = None
    first_failure_promotions = None
    for offset in range(0, len(raw), FRAME_BYTES):
        rgb = raw[offset:offset + FRAME_BYTES]
        try:
            state = telemetry(rgb)
        except AssertionError:
            if not states:
                continue  # Initial video clear, before the first managed frame.
            raise
        if states and state["frame"] < states[-1]["frame"]:
            raise AssertionError("Guest frame counter went backwards")
        if states and state["frame"] == states[-1]["frame"]:
            continue
        if state["mode"] not in (0, 1) or not 0 <= state["residentKiB"] <= 192 or state["peakResidentKiB"] > 192:
            raise AssertionError("Invalid mode or native cache exceeded 192 KiB")
        if state["coarseKiB"] != 6 or state["fallbackCells"] + state["detailCells"] != state["visibleCells"]:
            raise AssertionError("Fallback coverage/accounting mismatch")
        if state["visibleCells"] != 63:
            raise AssertionError("Unexpected camera guard window")
        if state["uploadedKiB"] > 4 * (state["frame"] + 1):
            raise AssertionError("Upload token budget exceeded")
        baseline = state["nativeKiB"] - state["residentKiB"]
        if native_base is None:
            native_base = baseline
        if baseline != native_base:
            raise AssertionError("Native resources leaked across eviction or mode change")
        if first_failure_promotions is None and state["failures"] > 0:
            first_failure_promotions = state["promotions"]
        coarse, detail = rendered_world(rgb, state)
        modes.add(state["mode"])
        if coarse:
            fallback_modes.add(state["mode"])
        if detail:
            detail_modes.add(state["mode"])
        positions.add((state["cameraX256"], state["cameraY256"]))
        states.append(state)
    if len(states) < 20 or states[-1]["frame"] < 360:
        raise AssertionError("Insufficient sustained streaming frames")
    if states[0]["mode"] != (1 if start_mode == "atlas" else 0):
        raise AssertionError("Wrong initial mode")
    if modes != {0, 1} or detail_modes != {0, 1} or fallback_modes != {0, 1}:
        raise AssertionError("Both modes must render actual detail and actual coarse fallback")
    last = states[-1]
    for name in ("evictions", "failures", "cancelled", "switches"):
        if last[name] <= 0:
            raise AssertionError("Missing exercise of " + name)
    if last["reversals"] < 2 or len(positions) < 10:
        raise AssertionError("Camera sweep/reversal did not execute")
    if first_failure_promotions is None or last["promotions"] <= first_failure_promotions:
        raise AssertionError("Source failures did not recover")
    return dict(streaming=True, sampledFrames=len(states), lastFrame=last["frame"],
                testedModes=sorted(modes), renderedDetailModes=sorted(detail_modes),
                renderedFallbackModes=sorted(fallback_modes), cameraPositions=len(positions),
                nativeBaselineKiB=native_base, peakDetailKiB=max(s["peakResidentKiB"] for s in states),
                final=last, samples=states)
