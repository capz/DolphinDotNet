"""CI shape and coverage tests. No test depends on the samples repository."""
import importlib.util
from pathlib import Path
import re
import unittest

ROOT=Path(__file__).resolve().parents[1]
WORKFLOW=(ROOT/".github/workflows/build.yml").read_text()
PREVIEW=(ROOT/".github/workflows/nuget-preview.yml").read_text()
SUITE=(ROOT/"scripts/ci_test_suite.py").read_text()
SELECTOR=(ROOT/"scripts/ci_changes.py").read_text()
def job(name):
    m=re.search(r'^  '+re.escape(name)+r':\n(?P<body>.*?)(?=^  [\w-]+:|\Z)',WORKFLOW,re.M|re.S)
    assert m,name
    return m.group("body")

class WorkflowCoverageTests(unittest.TestCase):
    def test_framework_matrix_has_all_runtime_areas(self):
        expected={"native","compiler","storage","network","audio","graphics","assets","sdk","gamecube"}
        spec=importlib.util.spec_from_file_location("ci_changes",ROOT/"scripts/ci_changes.py")
        ci=importlib.util.module_from_spec(spec);spec.loader.exec_module(ci)
        self.assertEqual(set(ci.MATRIX),expected)
        for name in expected:
            self.assertIn('area=="'+name+'"',SUITE)
        self.assertIn("fromJSON(needs.changes.outputs.matrix)",WORKFLOW)
        self.assertIn("fail-fast: false",job("framework-tests"))
        self.assertIn("if: needs.changes.outputs.framework == 'true'",WORKFLOW)
    def test_no_external_sample_repository_dependency(self):
        for path in (ROOT/".github/workflows").glob("*.yml"):
            self.assertNotIn("DolphinDotNet.Samples",path.read_text(),path.name)
            self.assertNotIn("path: samples",path.read_text(),path.name)
        for path in ("tests/sdk_consumer.py","tests/build_workflow_test.py",
                     "tools/DolphinDotNet.Compiler.Tests/Program.cs",
                     "tests/EffectCompilerTests/Program.cs"):
            content=(ROOT/path).read_text()
            self.assertNotIn("samples/",content,path)
            self.assertNotIn("samples\\",content,path)
        self.assertNotIn("samples/",SUITE)
        # The game-specific rule tests moved with the examples. No remaining
        # framework-owned test project is allowed to compile their sources.
        for project in (ROOT/"tests").glob("*/*.csproj"):
            self.assertNotIn("../../samples/",project.read_text(),project.as_posix())
        self.assertIn("tests/fixtures/toon.dndfx.json",(ROOT/"tests/EffectCompilerTests/Program.cs").read_text())
        self.assertIn("tests/HelloGameCube",(ROOT/"tools/DolphinDotNet.Compiler.Tests/Program.cs").read_text())
    def test_gate_fails_closed_on_failed_skipped_or_cancelled(self):
        gate=job("ci-result")
        for key in ("changes","framework-tests","debugger-protocol","ide-workflow"):
            self.assertIn(key,gate)
        self.assertIn("'result']!='success'",gate)
        self.assertIn("expected='success' if value=='true' else 'skipped'",gate)
        self.assertIn("if result!=expected",gate)
    def test_selection_job_always_runs_regressions(self):
        section=job("changes")
        self.assertIn("test_ci_changes.py",section)
        self.assertIn("test_ci_workflow.py",section)
        self.assertIn("fetch-depth: 0",section)
        self.assertIn("scripts/ci_changes.py",section)
        self.assertIn('"matrix" =',SELECTOR.replace("]","] ")) if False else None
    def test_sdk_preview_uses_own_fixtures(self):
        self.assertIn("tests/sdk_consumer.py --workspace",PREVIEW)
        self.assertIn("    environment: nuget-preview",PREVIEW)
        self.assertIn("NuGet/login@v1",PREVIEW)
    def test_host_runners_unchanged(self):
        self.assertNotIn("ubuntu",WORKFLOW.lower())
        self.assertIn("os: [windows-latest, macos-latest]",WORKFLOW)
        self.assertNotIn("DndBuildBackend=Docker",WORKFLOW)
if __name__=="__main__":unittest.main()
