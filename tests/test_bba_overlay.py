import importlib.util
from pathlib import Path
import unittest
ROOT=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location('bba',ROOT/'scripts/build_bba_overlay.py');bba=importlib.util.module_from_spec(spec);spec.loader.exec_module(bba)
class WindowBounds(unittest.TestCase):
 def test_reject_overflow_and_small_window_before_source_access(self):
  for window in (0,2919,65536,1000000):
   with self.assertRaises(ValueError):bba.prepare(Path('/does-not-exist'),Path('/unused'),window)
if __name__=='__main__':unittest.main()
