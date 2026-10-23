"""Check reproducible debugger fetching and preservation of local source edits."""
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('prepare_debugger', Path(__file__).resolve().parents[1] / 'eng/prepare_debugger.py')
prepare = importlib.util.module_from_spec(spec)
spec.loader.exec_module(prepare)

class DependencyTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        root = Path(self.temp.name)
        self.remote = root / 'debugger source'
        self.remote.mkdir()
        self.git('init', str(self.remote))
        (self.remote / 'adapter.txt').write_text('reviewed source')
        self.git('-C', str(self.remote), 'add', '.')
        self.git('-C', str(self.remote), '-c', 'user.name=Dependency Test', '-c', 'user.email=test@example.invalid',
                 'commit', '-m', 'Reviewed adapter')
        self.revision = self.git('-C', str(self.remote), 'rev-parse', 'HEAD').stdout.strip()
        self.sdk = root / 'SDK with spaces'
        (self.sdk / 'eng').mkdir(parents=True)
        self.manifest = self.sdk / 'eng/debugger-source.json'
        self.write_manifest(self.revision)
        self.patcher = patch.object(prepare, 'ROOT', self.sdk)
        self.patcher.start()
        self.addCleanup(self.patcher.stop)

    def git(self, *args):
        return subprocess.run(['git', *args], check=True, text=True, capture_output=True)

    def write_manifest(self, revision):
        self.manifest.write_text(json.dumps(dict(repository=str(self.remote), revision=revision)))

    def test_exact_revision_is_fetched_and_reused(self):
        source = prepare.prepare()
        self.assertEqual('reviewed source', (source / 'adapter.txt').read_text())
        self.assertEqual(self.revision, self.git('-C', str(source), 'rev-parse', 'HEAD').stdout.strip())
        with patch.object(prepare.subprocess, 'run', wraps=subprocess.run) as commands:
            self.assertEqual(source, prepare.prepare())
            self.assertFalse(any('fetch' in c.args[0] for c in commands.call_args_list))

    def test_dirty_checkout_is_preserved(self):
        source = prepare.prepare()
        (source / 'adapter.txt').write_text('local work')
        with self.assertRaisesRegex(RuntimeError, 'local changes'):
            prepare.prepare()
        self.assertEqual('local work', (source / 'adapter.txt').read_text())

    def test_wrong_origin_and_non_git_directory_are_rejected(self):
        target = self.sdk / 'out/debuggers'
        target.mkdir(parents=True)
        with self.assertRaisesRegex(RuntimeError, 'non-Git'):
            prepare.prepare()
        target.rmdir()
        source = prepare.prepare()
        self.git('-C', str(source), 'remote', 'set-url', 'origin', 'unexpected-origin')
        with self.assertRaisesRegex(RuntimeError, 'origin'):
            prepare.prepare()

    def test_failed_initial_fetch_can_be_retried(self):
        self.write_manifest('f' * 40)
        with self.assertRaises(subprocess.CalledProcessError):
            prepare.prepare()
        self.write_manifest(self.revision)
        self.assertEqual('reviewed source', (prepare.prepare() / 'adapter.txt').read_text())

    def test_moving_branch_is_not_a_valid_pin(self):
        self.write_manifest('main')
        with self.assertRaisesRegex(ValueError, 'commit SHA'):
            prepare.prepare()

if __name__ == '__main__':
    unittest.main()
