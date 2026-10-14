"""Watchdog behavior independent of DNS/HTTP timing and the managed runtime."""
import contextlib
import io
import sys
import unittest

import network_fixtures


class NetworkFixtureTests(unittest.TestCase):
    def test_default_budget_and_child_arguments_are_preserved(self):
        options = network_fixtures.parse_options(['program', '--gc-stress'])
        self.assertEqual(options.timeout_seconds, 45)
        self.assertEqual(options.command, ['program', '--gc-stress'])

    def test_custom_budget_preserves_child_arguments(self):
        options = network_fixtures.parse_options(
            ['--timeout-seconds', '120', 'program', '--gc-stress'])
        self.assertEqual(options.timeout_seconds, 120)
        self.assertEqual(options.command, ['program', '--gc-stress'])

    def test_invalid_budgets_are_rejected(self):
        for value in ['0', '-1', 'nan', 'inf', 'invalid']:
            with self.subTest(value=value), contextlib.redirect_stderr(io.StringIO()):
                with self.assertRaises(SystemExit):
                    network_fixtures.parse_options(['--timeout-seconds', value, 'program'])

    def test_child_exit_code_is_preserved(self):
        self.assertEqual(network_fixtures.run_command(
            [sys.executable, '-c', 'raise SystemExit(7)'], 5), 7)

    def test_timeout_kills_and_reaps_child_with_diagnostic(self):
        command = [sys.executable, '-c', 'import time; time.sleep(60)']
        diagnostic = io.StringIO()
        with contextlib.redirect_stderr(diagnostic):
            self.assertEqual(network_fixtures.run_command(command, 0.05), 124)
        self.assertIn('timed out after 0.05s', diagnostic.getvalue())
        self.assertIn(sys.executable, diagnostic.getvalue())
        # subprocess.run waits for the killed process before returning.
        with self.assertRaises(ChildProcessError):
            import os
            os.waitpid(-1, os.WNOHANG)


if __name__ == '__main__':
    unittest.main()
