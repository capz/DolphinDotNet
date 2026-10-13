"""Unit tests for DOL-related emulator log filtering."""
import sys
from pathlib import Path
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))
from dolphin_smoke import check
from dolphin_gdb_probe import parse_stop_pc, resolve_symbols, probe
from tempfile import TemporaryDirectory
from types import SimpleNamespace
from unittest.mock import patch


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



class GuestEntryTests(unittest.TestCase):
    def test_stop_requires_guest_pc(self):
        self.assertEqual(parse_stop_pc("T0540:80004000;01:00000000;"), 0x80004000)
        with self.assertRaises(ValueError):
            parse_stop_pc("T05")

    def test_elf_symbol_resolution_is_strict(self):
        table = (" 1: 80004000 96 FUNC GLOBAL DEFAULT 1 main\n"
                 " 2: 80005000 48 FUNC GLOBAL DEFAULT 1 dnd_aot_entry\n"
                 " 3: 80006000 64 FUNC GLOBAL DEFAULT 1 dnd_value_ManagedGameCube_Program_Main_a123\n")
        with TemporaryDirectory() as folder:
            elf = Path(folder) / "test.elf"
            elf.touch()
            with patch("dolphin_gdb_probe.subprocess.run", return_value=SimpleNamespace(stdout=table)):
                self.assertEqual(len(resolve_symbols(elf, ["main", "dnd_aot_entry",
                    "dnd_value_ManagedGameCube_Program_Main_*"])), 3)
                with self.assertRaises(ValueError):
                    resolve_symbols(elf, ["nonexistent"])

    def test_breakpoints_require_correct_guest_pc(self):
        class FakeSocket:
            def __enter__(self): return self
            def __exit__(self, *args): pass
            def sendall(self, data): pass
        with patch("dolphin_gdb_probe.socket.create_connection", return_value=FakeSocket()), \
             patch("dolphin_gdb_probe._reply", side_effect=[
                 "T0540:80003100;01:00000000;", "OK",
                 "T0540:80004000;01:00000000;", "OK"]):
            result = probe(2159, [("main", 0x80004000)])
        self.assertTrue(result.success, result.message)
        self.assertIn("DND_GDB_ENTRY_REACHED main", result.message)
        with patch("dolphin_gdb_probe.socket.create_connection", return_value=FakeSocket()), \
             patch("dolphin_gdb_probe._reply", side_effect=[
                 "T0540:80003100;01:00000000;", "OK",
                 "T0540:80005000;01:00000000;"]):
            result = probe(2159, [("main", 0x80004000)])
        self.assertFalse(result.success)
        self.assertIn("expected main", result.message)


if __name__ == "__main__":
    unittest.main()
