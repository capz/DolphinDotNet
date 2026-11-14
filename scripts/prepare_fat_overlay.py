#!/usr/bin/env python3
"""Prepare the pinned libdvm/FatFs sources with the SDK's exFAT path bound."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess

LIBDVM_REVISION = '420b9ae7beff18c361cbbfbe7671cabdf3dd26a5'
FATFS_REVISION = '6a655ec1dfcca81e317da1ed69bbb479e32424ea'
EXFAT_PATH_DEPTH = 64
DVM_MODULES = ('dvm_disc', 'dvm_cache', 'dvm_volume', 'dvm_prober', 'dvm_libogc2')
FAT_MODULES = ('fat_wrappers', 'fat_driver')


def git(source, *arguments):
    return subprocess.check_output(['git', '-C', str(source), *arguments], text=True).strip()


def verify_source(source):
    source = Path(source).resolve()
    for folder, expected in ((source, LIBDVM_REVISION), (source / 'fatfs', FATFS_REVISION)):
        if git(folder, 'rev-parse', 'HEAD') != expected:
            raise ValueError('Unexpected pinned source revision: ' + str(folder))
        if git(folder, 'status', '--porcelain', '--untracked-files=no'):
            raise ValueError('Pinned source has tracked changes: ' + str(folder))
    return source


def replace_once(path, before, after):
    content = path.read_text()
    if content.count(before) != 1:
        raise ValueError('Pinned source patch no longer matches: ' + str(path))
    path.write_text(content.replace(before, after))


def prepare(source, destination):
    source = verify_source(source)
    destination = Path(destination).resolve()
    # Never replace an input checkout or an ancestor of it while preparing a
    # generated tree; native builds may share a read-only source cache.
    if destination == source or destination in source.parents or source in destination.parents:
        raise ValueError('Prepared output must be separate from the source checkout')
    if destination.exists():
        shutil.rmtree(destination)
    for folder in ('include', 'source', 'fatfs/source'):
        (destination / folder).mkdir(parents=True, exist_ok=True)
    files = ['COPYING', 'include/dvm.h', 'include/fat.h',
             'source/dvm_debug.h', 'source/fat_driver.h']
    files += ['source/' + name + '.c' for name in DVM_MODULES + FAT_MODULES]
    files += ['fatfs/source/' + name for name in ('ff.c', 'ff.h', 'ffconf.h', 'ffunicode.c', 'diskio.h')]
    for name in files:
        shutil.copyfile(source / name, destination / name)

    config = destination / 'fatfs/source/ffconf.h'
    before = config.read_text()
    after, count = re.subn(r'(?m)^#define FF_PATH_DEPTH[ \t]+10$',
                          '#define FF_PATH_DEPTH\t' + str(EXFAT_PATH_DEPTH), before)
    if count != 1:
        raise ValueError('Unexpected pinned FatFs path-depth configuration')
    config.write_text(after)

    # Preserve every original FRESULT value. This private appended result lets
    # a path-capacity failure reach .NET as PathTooLongException, rather than
    # falsely claiming the application ran out of RAM.
    replace_once(destination / 'fatfs/source/ff.h',
                 '\tFR_INVALID_PARAMETER\t/* (19) Given parameter is invalid */',
                 '\tFR_INVALID_PARAMETER,\t/* (19) Given parameter is invalid */\n'
                 '\tFR_PATH_TOO_DEEP\t\t/* SDK: exFAT path exceeds FF_PATH_DEPTH */')
    replace_once(destination / 'fatfs/source/ff.c',
                 '\t\t\t\tif (++fs->xcwds2.depth >= sizeof fs->xcwds2.tbl / sizeof fs->xcwds2.tbl[0]) {\t/* Is it too deep path? */\n'
                 '\t\t\t\t\tres = FR_NOT_ENOUGH_CORE; break;',
                 '\t\t\t\tif (++fs->xcwds2.depth >= sizeof fs->xcwds2.tbl / sizeof fs->xcwds2.tbl[0]) {\t/* Is it too deep path? */\n'
                 '\t\t\t\t\tres = FR_PATH_TOO_DEEP; break;')
    replace_once(destination / 'source/fat_driver.c',
                 '\t\t[FR_INVALID_PARAMETER]   = EINVAL,',
                 '\t\t[FR_INVALID_PARAMETER]   = EINVAL,\n'
                 '\t\t[FR_PATH_TOO_DEEP]       = ENAMETOOLONG,')
    manifest = {'libdvm': LIBDVM_REVISION, 'fatfs': FATFS_REVISION,
                'exfatPathDepth': EXFAT_PATH_DEPTH, 'tooDeepErrno': 'ENAMETOOLONG',
                'privateFatFsAbiRebuilt': True,
                'sources': {name: hashlib.sha256((destination / name).read_bytes()).hexdigest()
                            for name in sorted(files)}}
    (destination / 'source-manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
    return destination


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    options = parser.parse_args()
    print(prepare(options.source, options.output))
