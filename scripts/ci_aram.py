"""Execute managed-array and native-buffer ARAM through production AOT/runtime."""
from pathlib import Path
import os
import subprocess
import sys
import tempfile

ROOT=Path(__file__).resolve().parents[1]
def run(*args):
    print('+',' '.join(map(str,args)),flush=True)
    subprocess.run(list(map(str,args)),cwd=ROOT,check=True,
                   env={**os.environ,'DND_ASYNC_MANUAL':'1'})

def main():
    run('dotnet','build','tools/DolphinDotNet.Compiler','-c','Release','-p:DndBuildNative=false')
    compiler=ROOT/'tools/DolphinDotNet.Compiler/bin/Release/net8.0/DolphinDotNet.Compiler.dll'
    dead='-Wl,-dead_strip' if sys.platform=='darwin' else '-Wl,--gc-sections'
    with tempfile.TemporaryDirectory(prefix='dnd-aram-acceptance-') as temporary:
        root=Path(temporary)
        native=root/'broker-native'
        run('cc','-std=c11','-O2','-Wall','-Wextra','-Werror','-DDND_ARAM_TEST','-Iinclude',
            'tests/aram_broker_test.c','source/aram.c','-o',native)
        run(native)
        console_aliases=root/'broker-gekko-aliases'
        run('cc','-std=c11','-O2','-Wall','-Wextra','-Werror','-DDND_ARAM_TEST','-DGEKKO','-Iinclude',
            'tests/aram_broker_test.c','source/aram.c','-o',console_aliases)
        run(console_aliases)
        for project,host in (('AramSmoke','aram_host.c'),('AramBrokerSmoke','aram_broker_host.c')):
            for config in ('Debug','Release'):
                run('dotnet','build','tests/'+project,'-c',config,'-p:DndBuildNative=false')
                generated=root/(project+'-'+config+'.c');binary=generated.with_suffix('')
                run('dotnet',compiler,'--aot','tests/'+project+'/bin/'+config+'/net8.0/'+project+'.dll',generated)
                sources=['source/managed_runtime.c','source/async_runtime.c','source/aram.c','source/numeric.c','source/text.c']
                if project=='AramBrokerSmoke':
                    sources+=['source/draw_resources.c','source/graphics_device.c','source/performance.c','tests/graphics_backend.c']
                run('cc','-std=c11','-pthread','-O1','-ffunction-sections','-fdata-sections','-Iinclude',
                    generated,'tests/'+host,*sources,dead,'-lm','-o',binary)
                run(binary);run(binary,'--gc-stress')
if __name__=='__main__':main()
