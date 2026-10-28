"""Exercise the emitted Makefile's real flag dependencies using host tools."""
from pathlib import Path
import json
import os
import shutil
import subprocess
import sys
import tempfile
import time


def verify(makefile):
    with tempfile.TemporaryDirectory(prefix='dnd-native-flags-') as folder:
        root=Path(folder);devkit=root/'devkit';bundle=root/'bundle';commands=root/'commands.jsonl'
        (devkit/'libogc2').mkdir(parents=True);(bundle/'source').mkdir(parents=True)
        (bundle/'scripts').mkdir();(bundle/'vendor/libogc2').mkdir(parents=True)
        compiler=shutil.which('cc');assert compiler
        wrapper=root/'cc-wrapper'
        wrapper.write_text('#!'+sys.executable+'\nimport json,subprocess,sys\n'
            f'with open({str(commands)!r},"a") as f:f.write(json.dumps(sys.argv[1:])+"\\n")\n'
            f'sys.exit(subprocess.call([{compiler!r}]+sys.argv[1:]))\n')
        wrapper.chmod(0o755)
        # Reuse the complete emitted graph; substitute only platform tools.
        (devkit/'libogc2/gamecube_rules').write_text(f'CC := {wrapper}\n%.o: %.c\n\t$(CC) $(CFLAGS) -MMD -MP -c $< -o $@\n')
        shutil.copyfile(makefile,bundle/'Makefile')
        (bundle/'source/probe.c').write_text('int main(void){return 0;}\n')
        for name in ('build_exi_overlay.sh','verify_exi_source.py','dol_sections.py'):
            (bundle/'scripts'/name).write_text('')
        (bundle/'vendor/libogc2/exi.c').write_text('')
        overlay=bundle/'build/exi-overlay';overlay.mkdir(parents=True)
        (overlay/'libogc.a').write_bytes(b'')
        converter=root/'elf2dol'
        converter.write_text('#!'+sys.executable+'\nimport shutil,sys\nshutil.copyfile(sys.argv[1],sys.argv[2])\n');converter.chmod(0o755)
        env=dict(os.environ,DEVKITPRO=str(devkit),DEVKITPPC=str(devkit),PATH=str(root)+os.pathsep+os.environ['PATH'])
        def run(mode):
            subprocess.run(['make','-s','-C',str(bundle),f'DND_LTO={mode}','LIBS='],env=env,check=True)
            subprocess.run([str(bundle/'Game.elf')],check=True)
            return [json.loads(s) for s in commands.read_text().splitlines()]
        first=run(0);assert len(first)==2 and all('-flto' not in c for c in first)
        assert run(0)==first, 'Unchanged flags rebuilt or relinked'
        # Apple GNU make may compare timestamps at whole-second granularity.
        time.sleep(1.1)
        second=run(1);assert len(second)==4 and all('-flto' in c for c in second[2:])
        assert run(1)==second, 'Unchanged optimized build was not reused'
        time.sleep(1.1)
        third=run(0);assert len(third)==6 and all('-flto' not in c for c in third[4:])
        print('Native flag switching rebuilds objects and relinks; unchanged builds are reused.')


if __name__=='__main__':verify(Path(sys.argv[1]))
