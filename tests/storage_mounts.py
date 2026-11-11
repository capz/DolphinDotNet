#!/usr/bin/env python3
"""Execute native registry fixtures and the managed API through the real AOT bridge."""
from pathlib import Path
import subprocess
import sys
import tempfile
ROOT=Path(__file__).resolve().parents[1]
def run(*args):
    print('+',' '.join(map(str,args)),flush=True)
    subprocess.run(list(map(str,args)),cwd=ROOT,check=True)
def main():
    runtime=['source/storage.c','source/contiguous_write.c','source/file_optimize.c','source/file_extents.c','source/verified_copy.c',
             'source/timing.c','source/managed_runtime.c','source/async_runtime.c','source/stw.c','source/gc_stw.c','source/text.c','source/numeric.c','source/bcl.c']
    dead_strip='-Wl,-dead_strip' if sys.platform=='darwin' else '-Wl,--gc-sections'
    flags=['cc','-std=c11','-D_DEFAULT_SOURCE','-D_DARWIN_C_SOURCE','-Wall','-Wextra','-ffunction-sections','-fdata-sections','-Iinclude','-pthread']
    with tempfile.TemporaryDirectory(prefix='dnd-mounts-') as temp:
        work=Path(temp)
        run(*flags,"tests/mount_disc_gate_test.c","-o",work/"gate")
        run(work/"gate")
        common=[]
        for index,source in enumerate(runtime[1:]):
            obj=work/('runtime-'+str(index)+'.o')
            run(*flags,'-c',source,'-o',obj);common.append(obj)
        for optimize in ([],['-O3','-flto','-fno-strict-aliasing']):
            for platform in ([],['-DDND_GAMECUBE_STORAGE','-Itests/storage_registry_sdk']):
                binary=work/'registry'
                run(*flags,*optimize,*platform,'tests/mount_registry_test.c',runtime[0],*common,dead_strip,'-lm','-o',binary)
                run(binary)
        run('dotnet','build','tools/DolphinDotNet.Compiler','-c','Release','-v','quiet')
        run('dotnet','build','tests/MountRegistrySmoke','-c','Release','-v','quiet')
        generated=work/'mounts.c'
        run('dotnet','tools/DolphinDotNet.Compiler/bin/Release/net8.0/DolphinDotNet.Compiler.dll',
            '--aot','tests/MountRegistrySmoke/bin/Release/net8.0/MountRegistrySmoke.dll',generated)
        run(*flags,generated,'tests/mount_registry_host.c',runtime[0],*common,dead_strip,'-lm','-o',work/'managed')
        run(work/'managed');run(work/'managed','--gc-stress')
        # Regression coverage for existing file/card-facing Storage APIs.
        run('dotnet','build','tests/StorageSmoke','-c','Release','-v','quiet')
        legacy=work/'storage.c'
        run('dotnet','tools/DolphinDotNet.Compiler/bin/Release/net8.0/DolphinDotNet.Compiler.dll',
            '--aot','tests/StorageSmoke/bin/Release/net8.0/StorageSmoke.dll',legacy)
        run(*flags,legacy,'tests/storage_host.c',runtime[0],*common,dead_strip,'-lm','-o',work/'legacy')
        run(work/'legacy');run(work/'legacy','--gc-stress')
        if sys.platform!='darwin': # Existing fault fixture uses GNU ld --wrap.
            run(*flags,'tests/storage_faults.c',runtime[0],*common,dead_strip,
                '-Wl,--wrap=read','-Wl,--wrap=write','-Wl,--wrap=close','-lm','-o',work/'faults')
            run(work/'faults')

if __name__=='__main__':main()
