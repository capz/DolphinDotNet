"""Negative controls for the rendered-TEV acceptance oracle."""
import sys
from pathlib import Path
import unittest
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'scripts'))
from dolphin_shader_test import validate


def frame():
    rgb=bytearray(640*480*3)
    colors=((12,24,64),(30,90,158),(70,220,248))
    for x in range(34,191):
        intensity=round((x-34)*255/156)
        for shift,value in ((0,(intensity,)*3),(208,(50,110,150)),
                            (416,colors[0 if intensity<=80 else 1 if intensity<=180 else 2])):
            at=(347*640+x+shift)*3;rgb[at:at+3]=bytes(value)
    return rgb


class ShaderCaptureTests(unittest.TestCase):
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
