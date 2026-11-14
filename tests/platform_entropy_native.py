#!/usr/bin/env python3
"""Exercise the actual entropy core's bounded native adapter and failure paths.

Synthetic timer results verify control flow only. --real-clock is an optional
host integration probe, never a claim of physical GameCube entropy quality.
"""
import argparse
import os
from pathlib import Path
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
CORE = ROOT / 'source/jitterentropy'
MODULES = ('base', 'gcd', 'health', 'noise', 'sha3', 'status', 'timer')


def run(*arguments, **kwargs):
    print('+', ' '.join(map(str, arguments)), flush=True)
    return subprocess.run(list(map(str, arguments)), cwd=ROOT, check=True, **kwargs)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--real-clock', action='store_true')
    parser.add_argument('--sanitize', action='store_true')
    options = parser.parse_args()
    with tempfile.TemporaryDirectory(prefix='dnd-platform-entropy-') as folder:
        work = Path(folder)
        flags = ['cc', '-std=c11', '-D_POSIX_C_SOURCE=200809L', '-O0', '-fno-lto', '-fwrapv',
                 '-DDND_PLATFORM_ENTROPY',
                 '-DDND_PLATFORM_ENTROPY_TEST', '-Iinclude', '-Isource',
                 '-I' + str(CORE), '-I' + str(CORE / 'src'), '-Wall']
        if options.sanitize:
            flags += ['-fsanitize=address,undefined', '-fno-omit-frame-pointer']
        objects = []
        for module in MODULES:
            target = work / (module + '.o')
            run(*flags, '-c', CORE / 'src' / ('jitterentropy-' + module + '.c'), '-o', target)
            objects.append(target)
        for optimization in ([], ['-O3', '-flto', '-fno-strict-aliasing']):
            # The upstream timing-sensitive objects above retain O0/no-LTO even
            # when the application/adapter uses the console's optimized flags.
            for bridge in (False, True):
                binary = work / ('entropy' + ('-optimized' if optimization else '') +
                                 ('-bridge' if bridge else '-native'))
                variant = '-DDND_ENTROPY_BRIDGE_TEST' if bridge else '-DDND_PLATFORM_ENTROPY_STANDALONE'
                run(*flags, *optimization, variant, '-Wextra', '-Werror',
                    ROOT / 'tests/platform-entropy-native-fixture.c',
                    ROOT / 'source/platform_entropy.c', *objects, '-o', binary)
                env = dict(os.environ)
                if options.sanitize:
                    env['ASAN_OPTIONS'] = 'detect_leaks=1'
                arguments = [binary]
                if options.real_clock:
                    arguments.append('--real-clock')
                run(*arguments, env=env, timeout=30)
    print('Platform entropy native contract checks passed; no physical entropy qualification is claimed.')


if __name__ == '__main__':
    main()
