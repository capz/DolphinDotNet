"""Independent negative controls for real streaming capture acceptance."""
import math
from pathlib import Path
import sys
import unittest
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))
from dolphin_streaming_check import FIELDS, FRAME_BYTES, telemetry, rendered_world, validate_stream


def frame(number, *, mode=None, hole=False, budget=192):
    mode = (number // 240) % 2 if mode is None else mode
    step = number % 192
    cx = 8 + step * .25 if step < 96 else 32 - (step - 96) * .25
    cy = 12 + ((number // 192) % 2) * 8
    state = dict.fromkeys(FIELDS, 0)
    state.update(magic=0x5453, version=2, frame=number, mode=mode,
                 cameraX256=int(cx * 256), cameraY256=int(cy * 256),
                 residentKiB=budget, peakResidentKiB=budget, promotions=number // 4,
                 evictions=max(0, number // 4 - 48), fallbackCells=20, detailCells=43,
                 failures=number // 40, cancelled=number // 120, switches=number // 240,
                 reversals=number // 96, uploadedKiB=number, visibleCells=63,
                 nativeKiB=500 + budget, coarseKiB=6)
    rgb = bytearray(FRAME_BYTES)
    scale = 240 / (5.5 * math.tan(math.radians(55 / 2)))
    for dy in range(-1, 2):
        for dx in range(-2, 3):
            x, y = int(cx) + dx, int(cy) + dy
            px = round(320 + (x + .375 - cx) * scale)
            py = round(240 - (y + .375 - cy) * scale)
            if not (20 <= px < 620 and 150 <= py < 420):
                continue
            lo = (18 + x % 12 * 8, 26 + y % 12 * 8, 84)
            hi = (45 + x % 12 * 12, 65 + y % 12 * 11, 184)
            color = hi if (x * 3 + y) & 1 else lo
            if dx % 2 == 0:
                color = ((lo[0] + hi[0]) // 2, (lo[1] + hi[1]) // 2, 134)
            at = (py * 640 + px) * 3
            if not hole:
                rgb[at:at + 3] = bytes(color)
    for f, name in enumerate(FIELDS):
        for bit in range(16):
            index = f * 16 + bit
            shade = 240 if state[name] & (1 << bit) else 16
            at = ((450 + index // 64 * 4) * 640 + index % 64 * 10 + 5) * 3
            rgb[at:at + 3] = bytes((shade,) * 3)
    return bytes(rgb)


class StreamingOracleTests(unittest.TestCase):
    def test_guest_strip_decodes(self):
        self.assertEqual(400, telemetry(frame(400))["frame"])

    def test_real_fallback_and_detail_are_checked(self):
        rgb = frame(400)
        self.assertTrue(all(n > 0 for n in rendered_world(rgb, telemetry(rgb))))

    def test_telemetry_without_rendered_world_fails(self):
        rgb = frame(400, hole=True)
        with self.assertRaises(AssertionError):
            rendered_world(rgb, telemetry(rgb))

    def test_sweeps_reversals_evictions_recovery_and_switch(self):
        proof = validate_stream(b"".join(frame(i) for i in range(0, 401, 16)), "pages")
        self.assertEqual([0, 1], proof["testedModes"])

    def test_static_or_single_mode_capture_cannot_pass(self):
        for frames in ([frame(0)] * 30, [frame(i, mode=0) for i in range(0, 401, 16)]):
            with self.assertRaises(AssertionError):
                validate_stream(b"".join(frames), "pages")

    def test_over_budget_fails(self):
        with self.assertRaises(AssertionError):
            validate_stream(frame(0, budget=196), "pages")

    def test_late_error_screen_fails(self):
        with self.assertRaises(AssertionError):
            validate_stream(frame(0) + bytes(FRAME_BYTES), "pages")

    def test_incomplete_frame_fails(self):
        with self.assertRaises(ValueError):
            validate_stream(b"x", "pages")


if __name__ == "__main__":
    unittest.main()
