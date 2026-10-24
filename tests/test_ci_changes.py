"""Selector regression tests: no samples checkout, full changed-path diff, fail-open."""
import importlib.util
from pathlib import Path
import subprocess
import unittest
from unittest.mock import patch
p=Path(__file__).resolve().parents[1]/"scripts/ci_changes.py"
spec=importlib.util.spec_from_file_location("ci_changes",p)
ci=importlib.util.module_from_spec(spec)
spec.loader.exec_module(ci)

class CiSelectionTests(unittest.TestCase):
    def test_docs_only(self):
        self.assertEqual(ci.select(["README.md","docs/guide.md"]),set())
    def test_native_dependency_propagation(self):
        actual=ci.select(["source/managed_runtime.c"])
        for key in ("native","compiler","storage","network","audio","graphics","gamecube"):
            self.assertIn(key,actual)
    def test_core_managed_api_propagation(self):
        actual=ci.select(["src/DolphinDotNet.Core/Dolphin/Collection/List.cs"])
        for key in ("compiler","storage","network","audio","graphics","assets","sdk","gamecube"):
            self.assertIn(key,actual)
    def test_scoped_domains(self):
        for path,expected in (
            ("source/audio.c",{"native","audio","gamecube"}),
            ("source/network.c",{"native","network","gamecube"}),
            ("source/draw_resources.c",{"native","graphics","gamecube"}),
            ("tests/AudioAssetTests/Program.cs",{"audio"}),
            ("tests/ModelAssetTests/Program.cs",{"graphics"}),
            ("tests/SaveImageTests/Program.cs",{"assets"}),
            ("tests/NetworkSmoke/Program.cs",{"network"}),
            ("tests/HelloGameCube/Program.cs",{"compiler"}),
            ("tools/DolphinDotNet.Compiler/Program.cs",ci.COMPILER),
            ("tests/DolphinLaunchProbe/Program.cs",{"sdk","ide","gamecube"}),
        ):
            with self.subTest(path=path):
                self.assertTrue(expected.issubset(ci.select([path])), (path,ci.select([path])))
                self.assertNotEqual(ci.select([path]),ci.ALL, path)
    def test_debugger_remains_external_compatibility(self):
        self.assertEqual(ci.select(["eng/debugger-source.json"]),{"debugger","sdk","ide"})
        self.assertIn("debugger",ci.select(["source/debug_heartbeat.c"]))
        self.assertIn("ide",ci.select(["scripts/dnd_debug_addresses.py"]))
    def test_deleted_samples_and_debugger_files_are_conservative(self):
        self.assertIn("ide",ci.select(["samples/deleted.cs"]))
        for path in ("plugins/rider/deleted.java","tools/DolphinDotNet.DebugAdapter/deleted.cs"):
            self.assertEqual(ci.select([path]),ci.ALL)
    def test_unknown_and_infrastructure_changes_run_all(self):
        for path in ("new-component/code.cs",".github/workflows/build.yml",
                     "scripts/ci_changes.py","scripts/ci_test_suite.py",
                     "tests/test_ci_changes.py","global.json"):
            with self.subTest(path=path):self.assertEqual(ci.select([path]),ci.ALL)
    def test_differential_union(self):
        result=ci.select(["source/audio.c","tests/ModelAssetTests/Program.cs"])
        self.assertTrue({"audio","graphics","native","gamecube"}.issubset(result))
    def test_main_dispatch_and_missing_diff_run_all(self):
        for event in ("push","workflow_dispatch","merge_group",""):
            self.assertEqual(ci.selection(event,{})[0],ci.ALL)
        self.assertEqual(ci.selection("pull_request",{})[0],ci.ALL)
    def test_compare_uses_merge_base_and_includes_deleted_old_paths(self):
        event={"pull_request":{"base":{"sha":"a"*40},"head":{"sha":"b"*40}}}
        with patch.object(ci.subprocess,"check_output",return_value=b"source/old.c\0docs/renamed.md\0") as mocked:
            self.assertIn("native",ci.selection("pull_request",event)[0])
            args=mocked.call_args.args[0]
            self.assertIn("--no-renames",args)
            self.assertIn("a"*40+"..."+"b"*40,args)
        with patch.object(ci.subprocess,"check_output",side_effect=subprocess.CalledProcessError(1,"git")):
            self.assertEqual(ci.selection("pull_request",event)[0],ci.ALL)
    def test_group_matrix_is_complete(self):
        self.assertEqual(set(ci.MATRIX)|{"debugger","ide"},ci.ALL)
        self.assertEqual(len(ci.MATRIX),9)

if __name__=="__main__":unittest.main()
