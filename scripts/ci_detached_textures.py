"""Exercise native lifetime guards and the managed owner through actual AOT."""
from pathlib import Path
import tempfile
import sys
from ci_graphics_phase_one import main,run
if __name__=='__main__':
    with tempfile.TemporaryDirectory(prefix='dnd-detached-') as folder:
        binary=Path(folder)/'ranges'
        dead='-Wl,-dead_strip' if sys.platform=='darwin' else '-Wl,--gc-sections'
        run('cc','-std=c11','-Wall','-Wextra','-Werror','-O1','-ffunction-sections','-fdata-sections','-Iinclude','tests/detached_texture_test.c','source/draw_resources.c',dead,'-lm','-o',binary)
        run(binary)
    main('DetachedTextureUploadSmoke','detached_texture_host.c')
