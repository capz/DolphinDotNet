#!/usr/bin/env python3
"""Generated selected-wave vectors through the real managed/native bridge.
No retail firmware, reference samples or proprietary audio are used.
"""
import os
from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]

def run(*args):
    subprocess.run(list(map(str, args)), cwd=ROOT, check=True)

def main():
    with tempfile.TemporaryDirectory() as tmp:
        work = Path(tmp)
        run('dotnet', 'run', '--project', 'tests/SystemFirmwareTests', '-c', 'Release',
            '-p:DndBuildNative=false', '--', work)
        binary = work / 'menu-audio'
        run(os.environ.get('CC', 'cc'), '-D_DARWIN_C_SOURCE', '-DDND_MENU_AUDIO_ONLY',
            '-std=c11', '-O1', '-Wall', '-Wextra', '-Werror', '-Wno-unused-function',
            '-Iinclude', 'tests/audio_formats_test.c', 'source/audio.c', '-lm', '-o', binary)
        run(binary, work)

if __name__ == '__main__':
    main()
