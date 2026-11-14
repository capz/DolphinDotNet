#!/usr/bin/env python3
"""Rebuild matching libdvm and FatFs archives for the SDK's exFAT path bound."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import tempfile

from prepare_fat_overlay import (DVM_MODULES, FAT_MODULES,
                                 LIBDVM_REVISION, prepare, verify_source)


def run(*arguments, cwd=None):
    subprocess.run(list(map(str, arguments)), cwd=cwd, check=True)


def checkout(source):
    if not (source / '.git').exists():
        source.parent.mkdir(parents=True, exist_ok=True)
        run('git', 'clone', '--quiet', '--filter=blob:none', '--no-checkout',
            'https://github.com/extremscorner/libdvm.git', source)
        run('git', 'fetch', '--quiet', '--depth=1', 'origin', LIBDVM_REVISION, cwd=source)
        run('git', 'checkout', '--quiet', '--detach', LIBDVM_REVISION, cwd=source)
        run('git', 'submodule', 'update', '--init', '--depth=1', 'fatfs', cwd=source)
    verify_source(source)


def build(source, output, devkitpro, devkitppc):
    checkout(source)
    output.mkdir(parents=True, exist_ok=True)
    sdk = devkitpro / 'libogc2/gamecube'
    tools = devkitppc / 'bin'
    cc, ar, ranlib = (tools / ('powerpc-eabi-' + name) for name in ('gcc', 'ar', 'ranlib'))
    with tempfile.TemporaryDirectory(prefix='fat-overlay-', dir=output.parent) as temporary:
        work = Path(temporary)
        tree = prepare(source, work / 'source')
        objects = work / 'objects'; objects.mkdir()
        flags = ['-O2', '-g', '-Wall', '-DGEKKO', '-DHW_DOL', '-mogc',
                 '-mcpu=750', '-meabi', '-msdata=sysv', '-mhard-float',
                 '-ffunction-sections', '-fdata-sections', '-fno-strict-aliasing',
                 '-DLIBDVM_BUFFER_ALIGN=32', '-DLIBDVM_WITH_ALIGNED_ACCESS',
                 '-I' + str(tree / 'include'), '-I' + str(tree / 'source'),
                 '-I' + str(tree / 'fatfs/source'), '-I' + str(sdk / 'include')]
        dvm_objects, fat_objects = [], []
        for module in DVM_MODULES + FAT_MODULES + ('ff', 'ffunicode'):
            folder = 'fatfs/source' if module in ('ff', 'ffunicode') else 'source'
            target = objects / (module + '.o')
            run(cc, *flags, '-c', tree / folder / (module + '.c'), '-o', target)
            (dvm_objects if module in DVM_MODULES else fat_objects).append(target)
        # Every private FATFS user is rebuilt together, including fat_driver's
        # embedded FatVolume allocation. Mixing it with the old system ff.o
        # after increasing FF_PATH_DEPTH would corrupt that object's layout.
        for library, modules in (('libdvm.a', dvm_objects),
                                 ('libfat.a', dvm_objects + fat_objects)):
            run(ar, 'rcs', work / library, *modules)
            run(ranlib, work / library)
        manifest = json.loads((tree / 'source-manifest.json').read_text())
        manifest['toolchain'] = subprocess.check_output([str(cc), '--version'], text=True).splitlines()[0]
        (work / 'build.json').write_text(json.dumps(manifest, indent=2) + '\n')
        # Publish libfat last: the Makefile uses it as the completed-pair marker.
        for name in ('libdvm.a', 'build.json', 'libfat.a'):
            os.replace(work / name, output / name)
    print('DND_FAT_SOURCE_VERIFIED')
    print('DND_FAT_OVERLAY_READY')


def main():
    root = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path,
                        default=Path(os.environ.get('DND_FAT_SOURCE_DIR', root / 'build/fat-upstream')))
    parser.add_argument('--output', type=Path, default=root / 'build/fat-overlay')
    options = parser.parse_args()
    build(options.source.resolve(), options.output.resolve(), Path(os.environ['DEVKITPRO']),
          Path(os.environ['DEVKITPPC']))


if __name__ == '__main__':
    main()
