"""Independent controls for the nine-case, single-DOL comparison acceptance."""
import sys
from pathlib import Path
import unittest
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))
from dolphin_benchmark_check import MEAN_FIELDS, P99_FIELDS, PIPELINE_FIELDS, TIMING_FIELDS, timing_telemetry, FIELDS, coverage_ceiling, validate_benchmark
from test_dolphin_streaming import frame


def benchmark_frame(case, complete=True, *, over_budget=False, holes=False, static=None, layout=None):
    group, budget = layout or ((1, 2, 4)[case % 3], (192, 256, 384)[case // 3])
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


def pipeline_frame(case, layout=None, **overrides):
    rgb = bytearray(benchmark_frame(case, layout=layout))
    values = dict.fromkeys(PIPELINE_FIELDS, 0)
    values.update(pipelineMagic=0x4250, pipelineVersion=1, pipelineCase=case,
                  optimizationMode=7, preparationDepth=2, publicationCap=2,
                  started=62, published=60, reused=20, maxPublications=2,
                  scratchKiB=128 if case % 3 == 2 else 84)
    values.update(overrides)
    for field, name in enumerate(PIPELINE_FIELDS):
        for bit in range(16):
            index = field * 16 + bit
            at = ((434 + index // 64 * 4) * 640 + index % 64 * 10 + 5) * 3
            rgb[at:at + 3] = bytes((240 if values[name] & (1 << bit) else 16,) * 3)
    return bytes(rgb)


def diagnostic_frame(case, fields, **values):
    rgb=bytearray(benchmark_frame(case))
    for field,name in enumerate(fields):
        for bit in range(16):
            index=field*16+bit
            at=((434+index//64*4)*640+index%64*10+5)*3
            rgb[at:at+3]=bytes((240 if values.get(name,0)&(1<<bit) else 16,)*3)
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

    def test_pipeline_requires_both_frozen_pages_for_each_case(self):
        raw = b"".join([frame(i) for i in range(5)] +
                       [f for i in range(9) for f in (pipeline_frame(i), benchmark_frame(i))] +
                       [frame(i) for i in range(5)])
        result = validate_benchmark(raw, pipeline=True)
        self.assertEqual(2, result["completedCases"][0]["maxPublications"])
        with self.assertRaisesRegex(AssertionError, "Missing completed pipeline"):
            validate_benchmark(capture(), pipeline=True)
        for bad in (dict(published=61), dict(maxPublications=3), dict(scratchKiB=129)):
            changed = raw.replace(pipeline_frame(3), pipeline_frame(3, **bad))
            with self.assertRaises(AssertionError):
                validate_benchmark(changed, pipeline=True)
        only_pipeline = b"".join([frame(i) for i in range(5)] +
                                  [pipeline_frame(i) for i in range(9)] + [frame(i) for i in range(5)])
        with self.assertRaisesRegex(AssertionError, "Missing completed stage timings"):
            validate_benchmark(only_pipeline, pipeline=True)

    def test_fixed_layout_matrix_modes_and_budget_are_checked(self):
        modes=(0,14,2,15,16,17,7,9,11)
        for budget in (256,384):
            raw=b"".join([frame(i) for i in range(5)]+
                         [f for i in range(9) for f in (benchmark_frame(i,layout=(2,budget)),
                          pipeline_frame(i,layout=(2,budget),optimizationMode=modes[i]))]+
                         [frame(i) for i in range(5)])
            self.assertEqual(9,len(validate_benchmark(raw,pipeline=True,matrix_budget=budget)["completedCases"]))
            with self.assertRaises(AssertionError):
                validate_benchmark(raw,pipeline=True,matrix_budget=384 if budget==256 else 256)
            wrong=raw.replace(pipeline_frame(3,layout=(2,budget),optimizationMode=modes[3]),
                              pipeline_frame(3,layout=(2,budget),optimizationMode=0))
            with self.assertRaisesRegex(AssertionError,"Wrong pipeline matrix mode"):
                validate_benchmark(wrong,pipeline=True,matrix_budget=budget)

    def test_extended_diagnostics_require_correlated_p99_and_bounded_queues(self):
        def pages(case,**bad):
            mean=dict(meanMagic=0x4251,meanVersion=1,meanCase=case,beginCopies=1,allocations=40,pendingPeak=2,readyPeak=2)
            mean.update({k:v for k,v in bad.items() if k in MEAN_FIELDS})
            p99=dict(p99Magic=0x4252,p99Version=1,p99Case=case,p99Samples=60,p99RankIndex=0,p99ExactTenthsMs=170,
                     p99PolicyTenthsMs=10,p99UploadTenthsMs=20,p99SubmitTenthsMs=10,p99EndTenthsMs=80,
                     p99GpuTenthsMs=10,p99ViTenthsMs=50,p99GapTenthsMs=30,p99OtherTenthsMs=20,p99Draws=24)
            p99.update({k:v for k,v in bad.items() if k in P99_FIELDS})
            return (benchmark_frame(case),pipeline_frame(case,pipelineVersion=3),
                    diagnostic_frame(case,MEAN_FIELDS,**mean),diagnostic_frame(case,P99_FIELDS,**p99))
        def proof(**bad):return b"".join([frame(i) for i in range(5)]+[p for case in range(9) for p in pages(case,**bad)]+[frame(i) for i in range(5)])
        result=validate_benchmark(proof(),pipeline=True)
        self.assertEqual(170,result['completedCases'][0]['p99ExactTenthsMs'])
        for bad in (dict(p99RankIndex=1),dict(p99Samples=59),dict(p99ExactTenthsMs=160),
                    dict(p99GpuTenthsMs=90),dict(pendingPeak=3),dict(beginCopies=2),dict(allocations=41)):
            with self.subTest(bad=bad),self.assertRaises(AssertionError):validate_benchmark(proof(**bad),pipeline=True)
        for phase in range(1400):
            self.assertEqual({0,1,2,3},{((phase+500*i)//350)%4 for i in range(8)})

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

    def test_completion_mask_does_not_replace_a_terminal_case_sample(self):
        # The failed Debug capture saw case 1 at 7993 ms, then case 2.
        # Later masks reached 511, but case 1's final measurements were absent.
        raw = b"".join([frame(i) for i in range(5)] +
                       [benchmark_frame(i, complete=(i != 1)) for i in range(9)] +
                       [frame(i) for i in range(5)])
        with self.assertRaisesRegex(AssertionError, r"Missing completed benchmark cases: \[1\]"):
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
