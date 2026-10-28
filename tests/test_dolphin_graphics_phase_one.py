from pathlib import Path
import sys,unittest
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'scripts'))
from dolphin_graphics_phase_one import PATCHES,FRAME_BYTES,validate_frame,validate_capture
class PhaseOnePixels(unittest.TestCase):
    def frame(self):
        rgb=bytearray(FRAME_BYTES)
        for _,x,y,color,_ in PATCHES:rgb[(y*640+x)*3:(y*640+x)*3+3]=bytes(color)
        for field,value in enumerate((0x4750,1,240,64,200,150,300,200)):
            for bit in range(16):
                index=field*16+bit;at=((450+(index//64)*4)*640+(index%64)*10+5)*3
                rgb[at:at+3]=bytes([240 if value&(1<<bit) else 16])*3
        return rgb
    def test_expected(self):validate_frame(self.frame())
    def test_every_patch_is_required(self):
        for name,x,y,color,_ in PATCHES:
            with self.subTest(patch=name):
                rgb=self.frame();rgb[(y*640+x)*3:(y*640+x)*3+3]=bytes((255,0,255)) if color==(0,0,0) else bytes(3)
                with self.assertRaises(AssertionError):validate_frame(rgb)
    def test_final_error_is_not_hidden(self):
        with self.assertRaises(AssertionError):validate_capture(bytes(self.frame())*60+bytes(FRAME_BYTES))
    def test_sustained(self):self.assertEqual(30,validate_capture(bytes(self.frame())*60)['validatedTailFrames'])
    def test_one_frame_rejected(self):
        with self.assertRaises(AssertionError):validate_capture(self.frame())
if __name__=='__main__':unittest.main()
