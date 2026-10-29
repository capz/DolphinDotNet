#!/usr/bin/env python3
"""Build pinned mbedTLS for the selected host or devkitPPC compiler.

The SDK downloads source over verified HTTPS on the build machine, never on the
console. --source permits an audited/offline checkout of the pinned revision.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shlex
import shutil
import subprocess
import tarfile
import tempfile
import urllib.request

REVISION='e185d7fd85499c8ce5ca2a54f5cf8fe7dbe3f8df' # mbedTLS 3.6.5 LTS

def build(output,cc,ar,cflags,config,source=None):
    output=Path(output).resolve();output.mkdir(parents=True,exist_ok=True)
    config=Path(config).resolve()
    library=output/'libdndtls.a';stamp=output/'configuration.json'
    identity=json.dumps([REVISION,cc,ar,cflags,hashlib.sha256(config.read_bytes()).hexdigest(),hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),subprocess.check_output(shlex.split(cc)+["--version"]).decode(errors="replace")])
    if library.is_file() and stamp.is_file() and stamp.read_text()==identity:return
    if source is None:
        source=output/'source'
        if not (source/'include/mbedtls/ssl.h').is_file():
            with tempfile.TemporaryDirectory(dir=output) as folder:
                archive=Path(folder)/'source.tar.gz'
                with urllib.request.urlopen(f'https://codeload.github.com/Mbed-TLS/mbedtls/tar.gz/{REVISION}',timeout=120) as reply:
                    with archive.open('wb') as target:shutil.copyfileobj(reply,target)
                with tarfile.open(archive) as tar:
                    # Extract only regular source/header/license files under the pinned root.
                    for entry in tar.getmembers():
                        parts=Path(entry.name).parts
                        if not entry.isfile() or len(parts)<2 or '..' in parts:continue
                        relative=Path(*parts[1:])
                        if relative.parts[0] not in ('library','include') and str(relative)!='LICENSE':continue
                        destination=source/relative;destination.parent.mkdir(parents=True,exist_ok=True)
                        with tar.extractfile(entry) as inp,destination.open('wb') as out:shutil.copyfileobj(inp,out)
    source=Path(source).resolve()
    flags=shlex.split(cflags)+['-I'+str(source/'include'),'-I'+str(config.parent),'-DMBEDTLS_CONFIG_FILE="'+config.name+'"']
    objects=[]
    for path in sorted((source/'library').glob('*.c')):
        obj=output/(path.stem+'.o')
        subprocess.run(shlex.split(cc)+flags+['-c',str(path),'-o',str(obj)],check=True)
        objects.append(str(obj))
    if not objects:raise RuntimeError('Missing mbedTLS sources')
    temporary=output/'libdndtls.tmp.a';temporary.unlink(missing_ok=True)
    subprocess.run(shlex.split(ar)+['rcs',str(temporary),*objects],check=True)
    temporary.replace(library);stamp.write_text(identity)

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--output',required=True);p.add_argument('--cc',default='cc');p.add_argument('--ar',default='ar');p.add_argument('--cflags',default='-O2');p.add_argument('--config',required=True);p.add_argument('--source')
    a=p.parse_args();build(a.output,a.cc,a.ar,a.cflags,a.config,a.source)
