"""Model acceptance scheduling must execute binaries, not merely build them."""
import importlib.util
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("ci_model_tests", ROOT / "scripts/ci_model_tests.py")
ci = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ci)


class ModelAcceptanceSchedulingTests(unittest.TestCase):
    def test_graphics_domain_invokes_model_acceptance(self):
        spec = importlib.util.spec_from_file_location("ci_suite", ROOT / "scripts/ci_test_suite.py")
        suite = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(suite)
        with patch.object(suite, "run") as run:
            suite.main("graphics")
        commands = [list(map(str, call.args)) for call in run.call_args_list]
        self.assertIn(["python3","scripts/ci_graphics_phase_one.py"],commands)
        self.assertIn(["python3","scripts/ci_graphics_phase_two.py"],commands)
        generated = next(i for i, c in enumerate(commands) if "tests/ModelAssetTests" in c)
        acceptance = next(i for i, c in enumerate(commands) if "scripts/ci_model_tests.py" in c)
        self.assertLess(generated, acceptance)
        self.assertEqual(commands[generated][-1], commands[acceptance][-1])

    def test_native_recording_and_aot_binaries_execute_on_macos(self):
        with tempfile.TemporaryDirectory() as tmp, patch.object(ci, "run") as run, patch.object(ci.sys, "platform", "darwin"):
            ci.main(tmp)
        commands = [list(map(str, call.args)) for call in run.call_args_list]
        for name in ("model-inflate", "model-native", "model-recording", "model-smoke"):
            self.assertTrue(any(Path(c[0]).name == name for c in commands), name)
        self.assertTrue(any(c[-1] == "--gc-stress" for c in commands))
        self.assertTrue(any("-Wl,-dead_strip" in c for c in commands))
        self.assertFalse(any("--wrap=" in arg for c in commands for arg in c))
        exceptions = [c for c in commands if "-Wno-unused-function" in c]
        self.assertEqual(len(exceptions), 1)
        self.assertIn("-c", exceptions[0])
        self.assertTrue(any(arg.endswith("model-smoke.c") for arg in exceptions[0]))
        for allocation in ("malloc", "calloc", "realloc", "aligned_alloc"):
            self.assertTrue(any(f"-D{allocation}=__wrap_{allocation}" in c for c in commands))
        smoke_runs = [i for i, c in enumerate(commands) if Path(c[0]).name == "model-smoke"]
        regeneration = next(i for i, c in enumerate(commands) if "tests/ModelAssetTests" in c)
        self.assertLess(smoke_runs[0], regeneration)
        self.assertLess(regeneration, smoke_runs[1])


if __name__ == "__main__":
    unittest.main()
