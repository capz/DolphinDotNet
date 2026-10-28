"""Negative controls for TextureStrategy's rendered-frame acceptance."""
import math
from pathlib import Path
import sys
import unittest
import shutil
from unittest.mock import Mock, patch
from tempfile import TemporaryDirectory
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))
from dolphin_texture_test import FRAME_BYTES, validate_board, validate_capture, decode_texture_capture
from dolphin_shader_test import run_ffmpeg


def board():
    rgb = bytearray(FRAME_BYTES)
    scale = 240 / (23 * math.tan(math.radians(55 / 2)))
    for cy in range(6, 10):
        for cx in range(2, 10):
            x = round(320 + (cx + .375 - 6) * scale)
            y = round(240 - (cy + .375 - 6) * scale)
            color = (45 + cx * 12, 65 + cy * 11, 184) if (cx * 3 + cy) & 1 else (18 + cx * 8, 26 + cy * 8, 84)
            at = (y * 640 + x) * 3
            rgb[at:at + 3] = bytes(color)
    return bytes(rgb)


class TextureAcceptanceTests(unittest.TestCase):
    def test_expected_board(self):
        self.assertEqual(32, validate_board(board()))

    def test_black_clear_and_error_screen_cannot_pass(self):
        for color in ((0, 0, 0), (8, 10, 19), (6, 9, 15)):
            with self.subTest(color=color), self.assertRaises(AssertionError):
                validate_board(bytes(color) * (640 * 480))

    def test_one_good_frame_does_not_pass(self):
        with self.assertRaises(AssertionError):
            validate_capture(board())

    def test_error_after_good_frames_fails(self):
        with self.assertRaises(AssertionError):
            validate_capture(board() * 59 + bytes(FRAME_BYTES))

    def test_startup_clear_then_sustained_board(self):
        result = validate_capture(bytes(FRAME_BYTES) * 30 + board() * 30)
        self.assertEqual(60, result["renderedFrames"])
        self.assertEqual(30, result["validatedTailFrames"])

    def test_truncated_frame_fails(self):
        with self.assertRaises(ValueError):
            validate_capture(board() * 60 + b"x")

    def test_benchmark_decode_deadline_scales_with_capture_and_stays_bounded(self):
        with TemporaryDirectory() as folder:
            log = Path(folder) / "decode.log"
            with patch("dolphin_shader_test.subprocess.run", return_value=Mock(stdout=b"", stderr=b"")) as run:
                for seconds, expected in ((30, 120), (180, 360), (360, 720), (10000, 720), (1e308, 720)):
                    with self.subTest(seconds=seconds):
                        decode_texture_capture(Path("benchmark.avi"), log, benchmark=True,
                                               capture_seconds=seconds)
                        self.assertEqual(expected, run.call_args.kwargs["timeout"])
                        # Keep sampling, output memory limits, and full-video validation.
                        command = run.call_args.args[0]
                        self.assertIn("fps=2,scale=640:480:flags=neighbor", command)
                        self.assertEqual("780", command[command.index("-frames:v") + 1])
                        self.assertNotIn("-sseof", command)
                decode_texture_capture(Path("default.avi"), log, benchmark=True)
                self.assertEqual(720, run.call_args.kwargs["timeout"])
                decode_texture_capture(Path("short.avi"), log, streaming=True)
                self.assertEqual(30, run.call_args.kwargs["timeout"])

    def test_invalid_benchmark_duration_does_not_start_decoder(self):
        with TemporaryDirectory() as folder:
            with patch("dolphin_texture_test.run_ffmpeg") as run:
                for seconds in (0, -1, float("inf"), float("nan")):
                    with self.subTest(seconds=seconds), self.assertRaises(ValueError):
                        decode_texture_capture(Path("invalid.avi"), Path(folder)/"decode.log",
                                               benchmark=True, capture_seconds=seconds)
                run.assert_not_called()

    @unittest.skipUnless(shutil.which("ffmpeg"), "FFmpeg not installed")
    def test_ffv1_capture_tail_rejects_late_error_screen(self):
        with TemporaryDirectory() as folder:
            root = Path(folder)
            for failing in (False, True):
                video = root / ("bad.avi" if failing else "good.avi")
                raw = board() * 90
                if failing:
                    raw += bytes(FRAME_BYTES) * 30
                run_ffmpeg(["-f", "rawvideo", "-pix_fmt", "rgb24", "-s", "640x480",
                            "-r", "60", "-i", "pipe:0", "-c:v", "ffv1", "-y", str(video)],
                           root / "encode.log", input=raw)
                decoded = decode_texture_capture(video, root / "decode.log")
                if failing:
                    with self.assertRaises(AssertionError):
                        validate_capture(decoded)
                else:
                    self.assertGreaterEqual(validate_capture(decoded)["renderedFrames"], 60)


if __name__ == "__main__":
    unittest.main()
