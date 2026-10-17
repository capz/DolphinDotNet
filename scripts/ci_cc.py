#!/usr/bin/env python3
"""Compile CI's multi-source GCC commands as cacheable objects, then link.

ccache owns dependency/compiler/flag validation. No object is trusted merely
because its filename exists, and each invocation has private link inputs.
"""
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile


def compile_command(arguments, compiler=None):
    compiler=compiler or os.environ.get('DND_CC_REAL','/usr/bin/cc')
    cache=shutil.which('ccache')
    prefix=([cache] if cache else [])+[compiler]
    if any(arg in arguments for arg in ('-include','-imacros','-x','-Xlinker','-L','-l','-specs')):
        subprocess.run(prefix+arguments,check=True);return
    # Leave preprocess/assembly/compile-only and unfamiliar modes to GCC.
    sources=[a for a in arguments if a.endswith(('.c','.S','.s')) and not a.startswith('-')]
    if not sources or any(a in arguments for a in ('-c','-E','-S','-shared')):
        subprocess.run(prefix+arguments,check=True);return
    output_positions=[i for i,a in enumerate(arguments) if a=='-o']
    if len(output_positions)!=1:
        subprocess.run(prefix+arguments,check=True);return
    output_index=output_positions[0]
    if output_index+1>=len(arguments):
        subprocess.run(prefix+arguments,check=True);return
    # Preserve compile flags exactly; only omit link inputs/options and output.
    compile_flags=[];skip=False
    for index,arg in enumerate(arguments):
        if skip: skip=False;continue
        if index==output_index: skip=True;continue
        if arg in sources or arg.endswith(('.o','.a')) or arg.startswith(('-l','-L','-Wl,','-fuse-ld=')):
            continue
        compile_flags.append(arg)
    with tempfile.TemporaryDirectory(prefix='dnd-ci-objects-',dir=os.environ.get('RUNNER_TEMP')) as directory:
        objects={}
        for index,source in enumerate(dict.fromkeys(sources)):
            target=str(Path(directory)/f'{index}.o')
            subprocess.run(prefix+compile_flags+['-c',source,'-o',target],check=True)
            objects[source]=target
        subprocess.run([compiler]+[objects.get(arg,arg) for arg in arguments],check=True)


def main():
    arguments=sys.argv[1:];compiler=None
    if arguments[:1]==['--compiler']:
        compiler=arguments[1];arguments=arguments[2:]
    try: compile_command(arguments,compiler)
    except subprocess.CalledProcessError as error: sys.exit(error.returncode)


if __name__=='__main__': main()
