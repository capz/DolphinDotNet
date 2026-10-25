"""Independent controls for the nine-case, single-DOL comparison acceptance."""
import sys
from pathlib import Path
import unittest
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))
from dolphin_benchmark_check import TIMING_FIELDS, timing_telemetry, FIELDS, coverage_ceiling, validate_benchmark
from test_dolphin_streaming import frame


def benchmark_frame(case, complete=True, *, over_budget=False, holes=False, static=None):
    group, budget = (1, 2, 4)[case % 3], (192, 256, 384)[case // 3]
    values = dict.fromkeys(FIELDS, 0)
    values.update(magic=0x424d, version=1, frame=case * 100 + (50 if complete else 1),
                  case=case, cameraX256=8 * 256, cameraY256=12 * 256,
                  residentKiB=budget + (4 if over_budget else 0), promotions=60,
                  coverageBasisPoints=5000, group=group, budgetKiB=budget,
                  elapsedMs=8100 if complete else 1000,
                  completedMask=(1 << (case + (1 if complete else 0))) - 1,
                  measuredMs=8000, staticCoverageBasisPoints=5000 if static is None else static,
                  requestedKiB=budget * 2 + 64, uploadedKiB=budget * 2,
                  nativeKiB=480 + budget, frameP95TenthsMs=167, frameP99TenthsMs=170,
                  motionCoverageBasisPoints=5000, peakResidentKiB=budget)
    rgb = bytearray(frame(0, hole=holes))
    for field, name in enumerate(FIELDS):
        for bit in range(16):
            index = field * 16 + bit
            shade = 240 if values[name] & (1 << bit) else 16
            at = ((450 + index // 64 * 4) * 640 + index % 64 * 10 + 5) * 3
            rgb[at:at + 3] = bytes((shade,) * 3)
    timing = dict.fromkeys(TIMING_FIELDS, 0)
    timing.update(timingMagic=0x4254, timingVersion=1, timingCase=case, timingSamples=60,
                  tailSamples=10, worstFrameTenthsMs=500, policyTenthsMs=10, uploadTenthsMs=50,
                  submitTenthsMs=30, endTenthsMs=200, gpuWaitTenthsMs=40, retraceWaitTenthsMs=150,
                  gapTenthsMs=190, otherTenthsMs=20, preparationTenthsMs=80, worstFrameDraws=24)
    for field, name in enumerate(TIMING_FIELDS):
        for bit in range(16):
            index = field * 16 + bit
            at = ((434 + index // 64 * 4) * 640 + index % 64 * 10 + 5) * 3
            rgb[at:at + 3] = bytes((240 if timing[name] & (1 << bit) else 16,) * 3)
    return bytes(rgb)


def capture():
    return b"".join([frame(i) for i in range(5)] +
                    [benchmark_frame(i) for i in range(9)] +
                    [frame(i) for i in range(5)])


class BenchmarkOracleTests(unittest.TestCase):
    def test_complete_suite_and_original_mode_round_trip(self):
        result = validate_benchmark(capture())
        self.assertEqual(9, len(result["completedCases"]))
        self.assertEqual([1, 2, 4], result["renderedGroups"])

    def test_stage_timings_are_required_and_exported(self):
        result = validate_benchmark(capture())
        self.assertEqual(190, result["completedCases"][0]["gapTenthsMs"])
        raw = bytearray(capture())
        start = 5 * 640 * 480 * 3 + 432 * 640 * 3
        raw[start:start + 16 * 640 * 3] = bytes(16 * 640 * 3)
        with self.assertRaises(AssertionError):
            validate_benchmark(bytes(raw))

    def test_independent_capacity_ceiling(self):
        self.assertEqual(7500, coverage_ceiling(dict(cameraX256=2048, cameraY256=3072, group=4, budgetKiB=192)))
        self.assertEqual(10000, coverage_ceiling(dict(cameraX256=2048, cameraY256=3072, group=1, budgetKiB=192)))

    def test_missing_case_cannot_pass(self):
        raw = b"".join([frame(i) for i in range(5)] +
                       [benchmark_frame(i) for i in range(9) if i != 4] + [frame(i) for i in range(5)])
        with self.assertRaises(AssertionError):
            validate_benchmark(raw)

    def test_over_budget_and_overstated_static_coverage_fail(self):
        for override in ({2: dict(over_budget=True)}, {2: dict(static=10000)}):
            raw = b"".join([frame(i) for i in range(5)] +
                           [benchmark_frame(i, **override.get(i, {})) for i in range(9)] + [frame(i) for i in range(5)])
            with self.assertRaises(AssertionError):
                validate_benchmark(raw)

    def test_counters_without_world_pixels_fail(self):
        with self.assertRaises(AssertionError):
            validate_benchmark(b"".join([frame(i) for i in range(5)] + [benchmark_frame(i, holes=True) for i in range(9)] + [frame(i) for i in range(5)]))

    def test_no_return_to_original_mode_fails(self):
        with self.assertRaises(AssertionError):
            validate_benchmark(b"".join([frame(i) for i in range(5)] + [benchmark_frame(i) for i in range(9)]))

    def test_incomplete_frame_fails(self):
        with self.assertRaises(ValueError):
            validate_benchmark(b"truncated")


if __name__ == "__main__":
    unittest.main()
