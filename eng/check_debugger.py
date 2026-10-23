#!/usr/bin/env python3
"""Check the SDK's debug metadata/native boundary against the pinned adapter."""
import os
import sys
from pathlib import Path
import subprocess
from prepare_debugger import prepare

ROOT = Path(__file__).resolve().parents[1]

def main():
    source = prepare()
    env = os.environ.copy()
    env['DND_RUNTIME_ROOT'] = str(ROOT)
    for command in (
        ['dotnet', 'run', '--project', str(source / 'tests/DebuggerTests'), '-c', 'Release'],
        ['dotnet', 'build', str(source / 'tools/DolphinDotNet.DebugAdapter'), '-c', 'Release'],
        [sys.executable, '-m', 'unittest', 'discover', '-s', str(source / 'tests'), '-p', 'test_debug_adapter.py'],
    ):
        subprocess.run(command, cwd=ROOT, env=env, check=True)

if __name__ == '__main__':
    main()
