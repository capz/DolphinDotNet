"""Run the additive graphics surface through the actual AOT/native runtime."""
from pathlib import Path
import os,subprocess,sys,tempfile
ROOT=Path(__file__).resolve().parents[1]
def run(*args):
    subprocess.run(list(map(str,args)),cwd=ROOT,check=True,env={**os.environ,'DND_ASYNC_MANUAL':'1'})
def main(project="GraphicsPhaseOneSmoke",host="graphics_phase_one_host.c",defines=()):
    run('dotnet','build','tools/DolphinDotNet.Compiler','-c','Release')
    with tempfile.TemporaryDirectory(prefix='dnd-graphics-phase-one-') as folder:
        p=Path(folder)
        for configuration in ('Debug','Release'):
            run('dotnet','build',f'tests/{project}','-c',configuration)
            generated=p/(configuration+'.c');exe=p/configuration
            run('dotnet','tools/DolphinDotNet.Compiler/bin/Release/net8.0/DolphinDotNet.Compiler.dll','--aot',f'tests/{project}/bin/{configuration}/net8.0/{project}.dll',generated)
            sources=['source/managed_runtime.c','source/async_runtime.c','source/numeric.c','source/text.c','source/draw_resources.c','source/graphics_device.c','source/performance.c','source/model_transform.c','source/model_graphics.c','source/model.c','source/model_animation.c']
            dead='-Wl,-dead_strip' if sys.platform=='darwin' else '-Wl,--gc-sections'
            run('cc','-pthread','-O1','-std=c11','-DDND_GRAPHICS_TEST',*defines,'-ffunction-sections','-fdata-sections','-Iinclude',generated,f'tests/{host}','tests/graphics_backend.c',*sources,dead,'-lm','-o',exe)
            run(exe)
if __name__=='__main__':main()
