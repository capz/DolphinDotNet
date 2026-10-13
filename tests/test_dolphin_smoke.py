"""Unit tests for DOL-related emulator log filtering."""
import sys
from pathlib import Path
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))
from dolphin_smoke import check
from dolphin_gdb_probe import parse_stop_pc, resolve_symbols, probe, probe_memory, MemoryExpectation, resolve_memory_expectations
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
                 "OK",
                 "T0540:80004000;01:00000000;", "OK"]):
            result = probe(2159, [("main", 0x80004000)])
        self.assertTrue(result.success, result.message)
        self.assertIn("DND_GDB_ENTRY_REACHED main", result.message)
        with patch("dolphin_gdb_probe.socket.create_connection", return_value=FakeSocket()), \
             patch("dolphin_gdb_probe._reply", side_effect=[
                 "OK",
                 "T0540:80005000;01:00000000;"]):
            result = probe(2159, [("main", 0x80004000)])
        self.assertFalse(result.success)
        self.assertIn("expected main", result.message)



class GuestMemoryTests(unittest.TestCase):
    def test_resolve_guest_data_symbols(self):
        table = (" 1: 8006fcb0 790 OBJECT LOCAL DEFAULT 11 lines\\n"
                 " 2: 8007ff00 4 OBJECT GLOBAL DEFAULT 11 dnd_control_main_reached\\n")
        with TemporaryDirectory() as folder:
            elf = Path(folder) / "test.elf"
            elf.touch()
            with patch("dolphin_gdb_probe.subprocess.run", return_value=SimpleNamespace(stdout=table)):
                watches = resolve_memory_expectations(elf,
                    ["dnd_control_main_reached=0x444e4443"],
                    ["lines=Hello from managed C# on GameCube"])
                self.assertEqual(len(watches), 2)
                self.assertEqual(watches[0].expected, b"DNDC")
                self.assertEqual(watches[1].address, 0x8006fcb0)


    def test_guest_ram_proof_requires_matching_bytes(self):
        class FakeSocket:
            def __enter__(self): return self
            def __exit__(self, *args): pass
            def sendall(self, data): pass
        watch = MemoryExpectation("main", 0x8007ff00, 4, b"DNDC")
        with patch("dolphin_gdb_probe.socket.create_connection", return_value=FakeSocket()):
            with patch("dolphin_gdb_probe.time.sleep"):
                with patch("dolphin_gdb_probe._reply",
                           side_effect=["T0540:8000528c;01:00000000;", "444e4443"]):
                    good = probe_memory(2159, [watch], seconds=5)
                with patch("dolphin_gdb_probe._reply",
                           side_effect=["T0540:8000528c;01:00000000;", "E00"]):
                    bad = probe_memory(2159, [watch], seconds=5)
        self.assertTrue(good.success, good.message)
        self.assertFalse(bad.success)

if __name__ == "__main__":
    unittest.main()
