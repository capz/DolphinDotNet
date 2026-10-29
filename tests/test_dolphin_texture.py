"""Negative controls for TextureStrategy's rendered-frame acceptance."""
import math
from pathlib import Path
import sys
import unittest
import shutil
import os
import signal
import subprocess
import time
from unittest.mock import Mock, patch
from tempfile import TemporaryDirectory
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))
from dolphin_texture_test import FRAME_BYTES, validate_board, validate_capture, decode_texture_capture
from dolphin_shader_test import run_ffmpeg
from dolphin_process import xvfb_command, stop_dolphin


class CaptureShutdownTests(unittest.TestCase):
    def test_graceful_stop_signals_dolphin_before_waiting_for_xvfb(self):
        with TemporaryDirectory() as folder:
            pid_file=Path(folder)/'dolphin.pid';pid_file.write_text('1235')
            process=Mock(pid=1234);process.poll.return_value=None
            events=[]
            with patch('dolphin_process.os.getpgid',return_value=1234), \
                 patch('dolphin_process.os.kill',side_effect=lambda *a:events.append(('signal',a))) as kill, \
                 patch('dolphin_process.os.killpg') as kill_group:
                process.wait.side_effect=lambda **kw:events.append(('wait',kw))
                stop_dolphin(process,pid_file)
                kill.assert_called_once_with(1235,signal.SIGTERM)
                kill_group.assert_not_called()
                self.assertEqual(['signal','wait'],[e[0] for e in events])

    def test_forced_shutdown_fails_capture_instead_of_accepting_its_tail(self):
        with TemporaryDirectory() as folder:
            pid_file=Path(folder)/'dolphin.pid';pid_file.write_text('1235')
            process=Mock(pid=1234);process.poll.return_value=None
            process.wait.side_effect=[subprocess.TimeoutExpired('dolphin',30),0]
            with patch('dolphin_process.os.getpgid',return_value=1234), \
                 patch('dolphin_process.os.kill'),patch('dolphin_process.os.killpg') as kill_group:
                with self.assertRaisesRegex(RuntimeError,'graceful capture shutdown'):
                    stop_dolphin(process,pid_file)
                kill_group.assert_called_once_with(1234,signal.SIGKILL)

    def test_foreign_pid_is_never_signalled(self):
        with TemporaryDirectory() as folder:
            pid_file=Path(folder)/'dolphin.pid';pid_file.write_text('1235')
            process=Mock(pid=1234);process.poll.return_value=None
            with patch('dolphin_process.os.getpgid',return_value=9999), \
                 patch('dolphin_process.os.kill') as kill,patch('dolphin_process.os.killpg'):
                with self.assertRaises(RuntimeError):stop_dolphin(process,pid_file)
                kill.assert_not_called()

    @unittest.skipUnless(shutil.which('xvfb-run'),'Xvfb not installed')
    def test_display_survives_emulator_cleanup_with_literal_paths(self):
        with TemporaryDirectory(prefix="capture $ ' ") as folder:
            root=Path(folder);ready=root/'ready';result=root/'result';pid_file=root/'pid'
            guest=root/'guest.py'
            guest.write_text('''import os,signal,socket,sys,time
from pathlib import Path
def stop(*args):
    time.sleep(.2)
    display=os.environ['DISPLAY'].rsplit(':',1)[1].split('.',1)[0]
    with socket.socket(socket.AF_UNIX) as s:s.connect('/tmp/.X11-unix/X'+display)
    Path(sys.argv[2]).write_text('display alive during final readback')
    sys.exit(0)
signal.signal(signal.SIGTERM,stop)
Path(sys.argv[1]).write_text('ready')
while True:time.sleep(.01)
''')
            process=subprocess.Popen(xvfb_command(sys.executable,[guest,ready,result],pid_file),
                                     start_new_session=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
            try:
                deadline=time.monotonic()+5
                while not ready.exists() and process.poll() is None and time.monotonic()<deadline:time.sleep(.01)
                self.assertTrue(ready.exists(),'Emulator wrapper did not start')
                stop_dolphin(process,pid_file)
                self.assertEqual(0,process.returncode)
                self.assertEqual('display alive during final readback',result.read_text())
            finally:
                if process.poll() is None:os.killpg(process.pid,signal.SIGKILL);process.wait()


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
                decode_texture_capture(Path("short.avi"), log)
                self.assertEqual(30, run.call_args.kwargs["timeout"])

    def test_streaming_decode_budget_covers_soak_and_keeps_sampling_limits(self):
        with TemporaryDirectory() as folder:
            log = Path(folder) / "decode.log"
            with patch("dolphin_shader_test.subprocess.run", return_value=Mock(stdout=b"", stderr=b"")) as run:
                for seconds, expected in ((5, 120), (90, 180), (120, 240), (10000, 240), (1e308, 240)):
                    with self.subTest(seconds=seconds):
                        decode_texture_capture(Path("streaming.avi"), log, streaming=True,
                                               capture_seconds=seconds)
                        self.assertEqual(expected, run.call_args.kwargs["timeout"])
                        command = run.call_args.args[0]
                        self.assertIn("fps=3,scale=640:480:flags=neighbor", command)
                        self.assertEqual("360", command[command.index("-frames:v") + 1])
                        self.assertNotIn("-sseof", command)

    def test_invalid_capture_duration_does_not_start_decoder(self):
        with TemporaryDirectory() as folder:
            with patch("dolphin_texture_test.run_ffmpeg") as run:
                for benchmark in (False, True):
                    for seconds in (0, -1, float("inf"), float("nan")):
                        with self.subTest(benchmark=benchmark, seconds=seconds), self.assertRaises(ValueError):
                            decode_texture_capture(Path("invalid.avi"), Path(folder)/"decode.log",
                                                   streaming=True, benchmark=benchmark, capture_seconds=seconds)
                run.assert_not_called()

    @unittest.skipUnless(shutil.which("ffmpeg"), "FFmpeg not installed")
    def test_streaming_ffv1_decode_preserves_start_and_late_failure(self):
        with TemporaryDirectory() as folder:
            root = Path(folder); video = root / "streaming.avi"
            run_ffmpeg(["-f", "rawvideo", "-pix_fmt", "rgb24", "-s", "640x480",
                        "-r", "60", "-i", "pipe:0", "-c:v", "ffv1", "-y", str(video)],
                       root / "encode.log", input=board() * 90 + bytes(FRAME_BYTES) * 30)
            raw = decode_texture_capture(video, root / "decode.log", streaming=True, capture_seconds=2)
            self.assertEqual(6 * FRAME_BYTES, len(raw))
            self.assertEqual(32, validate_board(raw[:FRAME_BYTES]))
            with self.assertRaises(AssertionError):validate_board(raw[-FRAME_BYTES:])

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
