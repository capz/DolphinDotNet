"""Negative controls for the rendered-TEV acceptance oracle."""
import sys
from pathlib import Path
import unittest
from unittest.mock import Mock,patch
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'scripts'))
from dolphin_shader_test import CAPTURE_TIMEOUT_SECONDS,validate,wait_for_capture


def frame():
    rgb=bytearray(640*480*3)
    colors=((12,24,64),(30,90,158),(70,220,248))
    for x in range(34,191):
        intensity=round((x-34)*255/156)
        for shift,value in ((0,(intensity,)*3),(208,(50,110,150)),
                            (416,colors[0 if intensity<=80 else 1 if intensity<=180 else 2])):
            at=(347*640+x+shift)*3;rgb[at:at+3]=bytes(value)
    for y,color in ((240,colors[2]),(275,colors[1]),(320,colors[0])):
        at=(y*640+528)*3;rgb[at:at+3]=bytes(color)
    at=(411*640+33)*3;rgb[at:at+3]=bytes((231,239,252))
    return rgb


class ShaderCaptureTests(unittest.TestCase):
    def test_slow_ci_startup_can_finish_after_thirty_seconds(self):
        process=Mock();process.poll.return_value=None
        frames=Mock();capture=Mock();capture.stat.return_value.st_size=512*1024
        frames.glob.return_value=[capture]
        with patch('dolphin_shader_test.time.monotonic',side_effect=[0,31]):
            self.assertIs(capture,wait_for_capture(process,frames,CAPTURE_TIMEOUT_SECONDS))

    def test_capture_deadline_remains_bounded_and_reports_partial_files(self):
        process=Mock();frames=Mock();capture=Mock();capture.name='partial.avi'
        capture.stat.return_value.st_size=1024;frames.glob.return_value=[capture]
        with patch('dolphin_shader_test.time.monotonic',side_effect=[0,91]):
            with self.assertRaisesRegex(TimeoutError,'partial.avi.*1024'):
                wait_for_capture(process,frames,CAPTURE_TIMEOUT_SECONDS)

    def test_emulator_exit_fails_without_waiting_for_deadline(self):
        process=Mock();process.poll.return_value=1
        with patch('dolphin_shader_test.time.monotonic',side_effect=[0,1]):
            with self.assertRaisesRegex(RuntimeError,'exited before rendering'):
                wait_for_capture(process,Mock(),CAPTURE_TIMEOUT_SECONDS)

    def test_three_real_effect_signatures(self):
        self.assertEqual([0,1,2],validate(frame())['toonBands'])

    def test_boot_or_clear_only_cannot_pass(self):
        with self.assertRaises(AssertionError):validate(bytes(640*480*3))

    def test_disabled_toon_stages_cannot_pass(self):
        rgb=frame()
        for x in range(34,191):
            source=(347*640+x)*3;target=source+416*3
            rgb[target:target+3]=rgb[source:source+3]
        with self.assertRaises(AssertionError):validate(rgb)

    def test_missing_ramp_cannot_pass(self):
        rgb=frame();at=(347*640+320)*3;rgb[at:at+3]=bytes((128,128,128))
        with self.assertRaises(AssertionError):validate(rgb)

    def test_incomplete_capture_cannot_pass(self):
        with self.assertRaises(ValueError):validate(b'')

    def test_missing_preset_cannot_pass(self):
        rgb=frame();at=(275*640+528)*3;rgb[at:at+3]=bytes((128,128,128))
        with self.assertRaises(AssertionError):validate(rgb)

    def test_leaked_hud_effect_cannot_pass(self):
        rgb=frame();at=(411*640+33)*3;rgb[at:at+3]=bytes((70,220,248))
        with self.assertRaises(AssertionError):validate(rgb)
