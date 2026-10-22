"""Guard Windows/macOS runners and require every selected CI job to pass."""
from contextlib import redirect_stdout
import io
import json
import os
from pathlib import Path
import re
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
WORKFLOW = (ROOT / '.github/workflows/build.yml').read_text()
GROUPS = {'debugger-protocol': 'debugger', 'ide-workflow': 'ide'}


def job(name):
    start = WORKFLOW.index('  ' + name + ':\n')
    end = re.search(r'^  [a-z][\w-]*:\s*$', WORKFLOW[start + 1:], re.M)
    return WORKFLOW[start:start + 1 + end.start()] if end else WORKFLOW[start:]


class WorkflowCoverageTests(unittest.TestCase):
    def test_all_workflows_use_only_windows_and_macos(self):
        for path in (ROOT / '.github/workflows').glob('*.yml'):
            text = path.read_text()
            with self.subTest(workflow=path.name):
                self.assertNotIn('ubuntu', text.lower())
                self.assertIsNone(re.search(r'^\s+container:', text, re.M))
                for runner in re.findall(r'^\s+runs-on: (.+)$', text, re.M):
                    if runner.startswith('${{'):
                        self.assertEqual('${{ matrix.os }}', runner)
                        self.assertIn('os: [windows-latest, macos-latest]', text)
                    else:
                        self.assertRegex(runner, r'^(?:windows|macos)-(?:latest|\d+(?:\.\d+)?(?:-intel)?)$')

    def test_removed_jobs_and_native_build_steps_are_not_selected(self):
        names = set(re.findall(r'^  ([\w-]+):$', WORKFLOW.split('jobs:\n', 1)[1], re.M))
        self.assertEqual({'changes', 'ci-result'} | set(GROUPS), names)
        self.assertNotIn('DndBuildBackend=Docker', WORKFLOW)
        self.assertIn('-p:DndBuildNative=false', job('ide-workflow'))
        outputs = set(re.findall(r'^      (\w+): \$\{\{ steps.select.outputs.', job('changes'), re.M))
        self.assertEqual(set(GROUPS.values()), outputs)

    def test_gate_requires_every_selected_result_and_selection_success(self):
        section = job('ci-result')
        self.assertIn('if: always()', section)
        for name in GROUPS:
            self.assertIn(name, section)
        code = section.split("python3 - <<'PY'\n", 1)[1].split('          PY', 1)[0]
        code = '\n'.join(line[10:] for line in code.splitlines())
        def check(results, succeeds):
            with patch.dict(os.environ, {'JOB_RESULTS': json.dumps(results)}), redirect_stdout(io.StringIO()):
                if succeeds:
                    exec(code, {})
                else:
                    with self.assertRaises((SystemExit, KeyError)):
                        exec(code, {})
        for selected in (True, False):
            outputs = {group: str(selected).lower() for group in GROUPS.values()}
            expected = 'success' if selected else 'skipped'
            results = {name: {'result': expected} for name in GROUPS}
            results['changes'] = {'result': 'success', 'outputs': outputs}
            check(results, True)
            for name in GROUPS:
                for outcome in ('success', 'failure', 'cancelled', 'skipped'):
                    with self.subTest(selected=selected, job=name, outcome=outcome):
                        check(dict(results, **{name: {'result': outcome}}), outcome == expected)
            check(dict(results, changes={'result': 'failure', 'outputs': outputs}), False)
            check(dict(results, changes={'result': 'success', 'outputs': {}}), False)

    def test_debugger_implementation_and_release_ownership_are_external(self):
        self.assertIn('python3 eng/check_debugger.py', job('debugger-protocol'))
        for path in ('plugins', 'tools/DolphinDotNet.DebugAdapter', 'tools/DolphinDotNet.Debugger',
                     'tools/DolphinDotNet.Debugger.Probe', 'tests/DebuggerTests',
                     '.github/workflows/rider.yml', '.github/workflows/visual-studio.yml'):
            self.assertFalse((ROOT / path).exists(), path)
        self.assertNotIn('release-extensions:', WORKFLOW)
        manifest = json.loads((ROOT / 'eng/debugger-source.json').read_text())
        self.assertEqual('https://github.com/capz/DolphinDotNet.Debuggers.git', manifest['repository'])
        self.assertRegex(manifest['revision'], r'^[0-9a-f]{40}$')

    def test_nuget_preview_keeps_consumer_check_and_protected_publication(self):
        preview = (ROOT / '.github/workflows/nuget-preview.yml').read_text()
        self.assertIn('tests/sdk_consumer.py --workspace', preview)
        self.assertNotIn('--native', preview)
        self.assertNotIn('external-gamecube', preview)
        self.assertIn('    needs: verify', preview)
        self.assertIn('    environment: nuget-preview', preview)
        self.assertIn('inputs.publish', preview)
        self.assertIn("github.ref == 'refs/heads/main'", preview)
        self.assertIn('NuGet/login@v1', preview)


if __name__ == '__main__':
    unittest.main()
