#!/usr/bin/env python3
"""Execute launch-content resolution through the real managed AOT/storage path.

Loose-content cases use native host volumes. Image cases use the production
GameCube mount registry and a host backend with the real portable ISO/FST
reader. This is not a physical launcher, EXI-adapter or throughput test.
"""
from pathlib import Path
import os
import struct
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
PAYLOAD = bytes((41, 42, 43, 44))
PROJECT = 'tests/StorageLaunchContentSmoke/StorageLaunchContentSmoke.csproj'
ASSEMBLY = 'tests/StorageLaunchContentSmoke/bin/Release/net8.0/StorageLaunchContentSmoke.dll'
COMPILER = 'tools/DolphinDotNet.Compiler/bin/Release/net8.0/DolphinDotNet.Compiler.dll'


def run(*arguments, cwd=ROOT, env=None, timeout=None):
    print('+', ' '.join(map(str, arguments)), flush=True)
    subprocess.run(list(map(str, arguments)), cwd=cwd, env=env, check=True, timeout=timeout)


def iso_bytes():
    """Small valid GameCube FST image; no game assets or external downloads."""
    data = bytearray(4096)
    def big(offset, value):
        struct.pack_into('>I', data, offset, value)
    data[:6] = b'TEST01'
    big(0x1c, 0xc2339f3d)
    big(0x424, 0x500)
    names = b'\0payload.bin\0'
    big(0x428, 24 + len(names))
    big(0x500, 0x01000000)
    big(0x508, 2)
    big(0x50c, 1)
    big(0x510, 0x800)
    big(0x514, len(PAYLOAD))
    data[0x518:0x518 + len(names)] = names
    data[0x800:0x800 + len(PAYLOAD)] = PAYLOAD
    return bytes(data)


def install(root, directory='apps/example', identity=7):
    app = root / directory
    app.mkdir(parents=True, exist_ok=True)
    (app / 'default.dol').write_bytes(b'launcher fixture')
    (app / 'payload.bin').write_bytes(PAYLOAD)
    (app / 'identity.bin').write_bytes(bytes((identity,)))
    (app / 'nested').mkdir(exist_ok=True)
    (app / 'nested/data.bin').write_bytes(PAYLOAD)
    for name in ('content.iso', 'content.gcm'):
        (app / name).write_bytes(iso_bytes())
    (app / 'corrupt.iso').write_bytes(b'not an ISO')
    for name in ('content.nkit.iso', 'content.rvz', 'content.gcz', 'content.ciso', 'content.wia'):
        (app / name).write_bytes(b'unsupported image fixture')


def prepare_volumes(work, scenario, image_backend):
    suffix = ':' if image_backend else ''
    for device in ('carda', 'cardb', 'sd', 'gcloader'):
        if device == 'carda' and scenario in (3, 4):
            continue
        install(work / (device + suffix), identity=1 if scenario == 3 and device == 'gcloader' else 7)
    if scenario == 1:
        install(work / ('gcloader' + suffix), 'relocated/My Town \u00fc')
    if scenario == 11:
        install(work / ('gcloader' + suffix), '/'.join(['a' * 180] * 3))
    if image_backend:
        dvd = work / 'dvd:'
        install(dvd)
        (dvd / 'payload.bin').write_bytes(PAYLOAD)
        if scenario in (23, 24):
            (work / 'physical-disc-present').write_bytes(b'present')
        if scenario == 28:
            for marker in ('fail-unmount-gcloader', 'fail-unmount-dvd'):
                (work / marker).write_bytes(b'fail once')


def main():
    run('dotnet', 'build', 'tools/DolphinDotNet.Compiler', '-c', 'Release', '-v', 'quiet')
    run('dotnet', 'build', PROJECT, '-c', 'Release', '-v', 'quiet')
    runtime = ('contiguous_write.c', 'file_optimize.c', 'file_extents.c', 'verified_copy.c',
               'timing.c', 'managed_runtime.c', 'async_runtime.c', 'stw.c', 'gc_stw.c',
               'text.c', 'numeric.c', 'bcl.c', 'launch.c')
    dead_strip = '-Wl,-dead_strip' if sys.platform == 'darwin' else '-Wl,--gc-sections'
    flags = ['cc', '-std=c11', '-D_DEFAULT_SOURCE', '-D_DARWIN_C_SOURCE', '-O2',
             '-Wall', '-Wextra', '-ffunction-sections', '-fdata-sections',
             '-DDND_CAPTURE_LAUNCH_ARGUMENTS', '-I' + str(ROOT / 'include'), '-pthread']
    with tempfile.TemporaryDirectory(prefix='dnd-launch-content-') as temporary:
        work = Path(temporary)
        generated = work / 'managed.c'
        run('dotnet', COMPILER, '--aot', ASSEMBLY, generated)
        objects = []
        for index, source in enumerate(runtime):
            obj = work / ('runtime-' + str(index) + '.o')
            run(*flags, '-c', ROOT / 'source' / source, '-o', obj)
            objects.append(obj)
        binaries = {}
        for image_backend in (False, True):
            binary = work / ('images' if image_backend else 'loose')
            extra = (['-DDND_GAMECUBE_STORAGE', '-DDND_TEST_LAUNCH_CONTENT_IMAGE',
                      '-I' + str(ROOT / 'tests/storage_registry_sdk'),
                      ROOT / 'tests/storage_launch_content_image_host.c',
                      ROOT / 'source/iso_file.c', ROOT / 'source/fst.c'] if image_backend else [])
            run(*flags, *extra, generated, ROOT / 'tests/storage_launch_content_host.c',
                ROOT / 'source/storage.c', *objects, dead_strip, '-lm', '-o', binary)
            binaries[image_backend] = binary
        count = 0
        for image_backend, scenarios in ((False, range(12)), (True, range(20, 29))):
            for stress in (False, True):
                for scenario in scenarios:
                    volume = work / ('volume-' + str(scenario) + '-' + str(int(stress)))
                    volume.mkdir()
                    prepare_volumes(volume, scenario, image_backend)
                    env = os.environ.copy()
                    env['DND_STORAGE_ROOT'] = str(volume)
                    hint = 'GCLDR:/relocated/My Town \u00fc/default.dol' if scenario == 1 else 'host:/harness.dol'
                    run(binaries[image_backend], scenario, hint, *(['--gc-stress'] if stress else []),
                        cwd=volume, env=env, timeout=30)
                    count += 1
        bundle = work / 'bundle'
        run('dotnet', COMPILER, '--bundle', ASSEMBLY, bundle)
        if '-DDND_CAPTURE_LAUNCH_ARGUMENTS' not in (bundle / 'Makefile').read_text():
            raise AssertionError('Launch-content resolver did not retain incoming argument capture')
        print(f'{count} launch-content AOT cases passed: explicit aliases, bounded fallbacks, validation, '
              'real file reads, ownership, generation changes, image dependency rollback and GC stress; '
              'physical launcher/adapter qualification remains separate')


if __name__ == '__main__':
    main()
