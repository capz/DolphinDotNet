"""Regression checks for CI selection and conservative dependency propagation."""
import importlib.util
from pathlib import Path
import subprocess
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("ci_changes", Path(__file__).resolve().parents[1] / "scripts/ci_changes.py")
ci = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ci)


class CiSelectionTests(unittest.TestCase):
    def test_docs_skip_builds(self):
        self.assertEqual(ci.select(["README.md", "docs/debugging.md"]), set())

    def test_debugger_pin_and_preparation_select_compatibility_and_ide(self):
        for path in ('eng/debugger-source.json', 'eng/prepare_debugger.py', 'eng/check_debugger.py'):
            self.assertEqual(ci.select([path]), {'debugger', 'ide'})

    def test_metadata_and_native_debug_contracts_select_both_consumers(self):
        for path in ('scripts/dnd_debug_addresses.py', 'source/debug_heartbeat.c', 'include/dnd_debug_heartbeat.h'):
            self.assertEqual(ci.select([path]), {'debugger', 'ide'})

    def test_native_runtime_selects_remaining_build_consumers(self):
        self.assertEqual(ci.select(["source/managed_runtime.c"]), ci.BUILD)

    def test_compiler_runs_all_managed_consumers(self):
        self.assertEqual(ci.select(["tools/DolphinDotNet.Compiler/Program.cs"]), ci.BUILD)

    def test_sdk_and_samples_run_managed_consumers(self):
        self.assertEqual(ci.select(["sdk/DolphinDotNet.Sdk/Sdk/Sdk.targets", "samples/Pong/Program.cs"]), ci.BUILD)

    def test_removed_debugger_paths_in_migration_diff_are_conservative(self):
        for path in ('plugins/rider/deleted.java', 'plugins/visual-studio/deleted.cs', 'tools/DolphinDotNet.DebugAdapter/deleted.cs'):
            self.assertEqual(ci.select([path]), set(ci.GROUPS))

    def test_ide_fixture_only(self):
        self.assertEqual(ci.select(["tests/DolphinLaunchProbe/Program.cs"]), {"ide"})

    def test_union(self):
        self.assertEqual(ci.select(["eng/debugger-source.json", "tests/build_workflow_test.py"]), {"debugger", "ide"})

    def test_unknown_workflow_and_selector_changes_run_everything(self):
        for path in ["new-component/main.cs", ".github/workflows/build.yml", "scripts/ci_changes.py", "global.json", "tests/test_ci_changes.py"]:
            with self.subTest(path=path):
                self.assertEqual(ci.select([path]), set(ci.GROUPS))

    def test_similarly_named_directories_are_not_excluded(self):
        self.assertEqual(ci.select(["docs-generator/code.cs"]), set(ci.GROUPS))

    def test_main_and_manual_runs_are_full(self):
        for event in ["push", "workflow_dispatch", "merge_group", ""]:
            self.assertEqual(ci.selection(event, {})[0], set(ci.GROUPS))

    def test_missing_or_invalid_pr_data_is_full(self):
        for data in [{}, {"pull_request": None}, {"pull_request": {"base": {"sha": "--bad"}, "head": {"sha": "f" * 40}}}]:
            self.assertEqual(ci.selection("pull_request", data)[0], set(ci.GROUPS))

    def test_diff_failure_is_full(self):
        event = {"pull_request": {"base": {"sha": "a" * 40}, "head": {"sha": "b" * 40}}}
        with patch.object(ci.subprocess, "check_output", side_effect=subprocess.CalledProcessError(1, "git")):
            self.assertEqual(ci.selection("pull_request", event)[0], set(ci.GROUPS))

    def test_full_pr_diff_preserves_old_and_new_paths(self):
        event = {"pull_request": {"base": {"sha": "a" * 40}, "head": {"sha": "b" * 40}}}
        with patch.object(ci.subprocess, "check_output", return_value=b"source/old.c\0docs/moved.md\0") as diff:
            self.assertEqual(ci.selection("pull_request", event)[0], ci.BUILD)
            args = diff.call_args.args[0]
            self.assertIn("--no-renames", args)
            self.assertIn("a" * 40 + "..." + "b" * 40, args)

    def test_empty_diff(self):
        self.assertEqual(ci.select([]), set())


if __name__ == "__main__":
    unittest.main()
