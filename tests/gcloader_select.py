#!/usr/bin/env python3
"""Run physical GC Loader selection storage preconditions and its real AOT bridge."""
from pathlib import Path
import subprocess
import sys
import tempfile

ROOT=Path(__file__).resolve().parents[1]

def run(*args):
    print('+',' '.join(map(str,args)),flush=True)
    subprocess.run(list(map(str,args)),cwd=ROOT,check=True)

def main():
    runtime=['source/contiguous_write.c','source/file_optimize.c','source/file_extents.c','source/verified_copy.c',
             'source/timing.c','source/managed_runtime.c','source/async_runtime.c','source/stw.c','source/gc_stw.c',
             'source/text.c','source/numeric.c','source/bcl.c']
    dead_strip='-Wl,-dead_strip' if sys.platform=='darwin' else '-Wl,--gc-sections'
    flags=['cc','-std=c11','-D_DEFAULT_SOURCE','-D_DARWIN_C_SOURCE','-Wall','-Wextra',
           '-ffunction-sections','-fdata-sections','-Iinclude','-pthread']
    with tempfile.TemporaryDirectory(prefix='dnd-gcloader-select-') as temp:
        work=Path(temp);objects=[]
        for index,source in enumerate(runtime):
            obj=work/('runtime-'+str(index)+'.o')
            run(*flags,'-c',source,'-o',obj);objects.append(obj)
        for optimize in ([],['-O3','-flto','-fno-strict-aliasing']):
            binary=work/'storage'
            run(*flags,*optimize,'-DDND_GAMECUBE_STORAGE','-Itests/storage_registry_sdk',
                'tests/gcloader_storage_test.c','source/storage.c',*objects,dead_strip,'-lm','-o',binary)
            run(binary)
        run('dotnet','build','tools/DolphinDotNet.Compiler','-c','Release','-v','quiet')
        run('dotnet','build','tests/GcLoaderSelectSmoke','-c','Release','-v','quiet')
        generated=work/'managed.c'
        run('dotnet','tools/DolphinDotNet.Compiler/bin/Release/net8.0/DolphinDotNet.Compiler.dll',
            '--aot','tests/GcLoaderSelectSmoke/bin/Release/net8.0/GcLoaderSelectSmoke.dll',generated)
        if 'dnd_storage_select_gcloader_disc_image(' not in generated.read_text():
            raise AssertionError('GC Loader select call did not lower to the native storage intrinsic')
        run(*flags,generated,'tests/gcloader_select_host.c','source/storage.c',*objects,
            dead_strip,'-lm','-o',work/'managed')
        run(work/'managed');run(work/'managed','--gc-stress')

if __name__=='__main__':main()
