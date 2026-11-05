import copy
import unittest
from check_system_firmware_captures import check

class FirmwareCaptureChecks(unittest.TestCase):
    def fixtures(self):
        names='environment tiles logo-top logo-left logo-right wordmark base-cube cover-cube logo wordmark-model small-cube menu-cube menu-icon-0 menu-icon-1 memory-cube drop trace-early trace-late assemble letters hold fade'.split()
        return [dict(png=name+'.png',sha256=f'{i:064x}',width=640,height=480,rawBytes=614400) for i,name in enumerate(names)]
    def test_distinct_original_views_and_stable_snapshots(self):
        records=self.fixtures();stable=copy.copy(records[1]);stable['png']='tiles-stable.png';records.append(stable)
        self.assertEqual(check(records),23)
        records[-1]['sha256']='f'*64
        with self.assertRaisesRegex(ValueError,'changed between frames'):check(records)
    def test_virtual_framebuffer_placeholder_rejected(self):
        records=self.fixtures()
        for record in records:record['sha256']='a'*64
        with self.assertRaisesRegex(ValueError,'not distinct'):check(records)
    def test_tev_alpha_fade_regression_rejected(self):
        records=self.fixtures();records[-1]['sha256']=records[-2]['sha256']
        with self.assertRaisesRegex(ValueError,'Fade did not change'):check(records)
    def test_missing_original_stage_rejected(self):
        with self.assertRaisesRegex(ValueError,'Missing capture: fade'):check(self.fixtures()[:-1])

if __name__=='__main__':unittest.main()
