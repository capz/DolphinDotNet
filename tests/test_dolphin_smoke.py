"""Unit tests for DOL-related emulator log filtering."""
import sys
from pathlib import Path
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))
from dolphin_smoke import check


class DolphinSmokeLogTests(unittest.TestCase):
    def test_alignment_warning_is_fatal(self):
        self.assertTrue(check("W[BOOT]: DolReader: Data section 0 size is not aligned to 32 bytes"))

    def test_loader_failure_is_fatal(self):
        self.assertTrue(check("Failed to load executable Game.dol"))

    def test_ppc_instruction_fault_is_fatal(self):
        self.assertTrue(check("ERROR: invalid instruction at 0x80003100"))

    def test_unrelated_warning_is_not_fatal(self):
        self.assertEqual([], check("W[AUDIO]: No audio device available"))

    def test_normal_log_is_not_fatal(self):
        self.assertEqual([], check("I[BOOT]: Boot completed"))


if __name__ == "__main__":
    unittest.main()
