#!/usr/bin/env python3
"""Generated IPL interpreter/mixer/cache/producer checks. No firmware required."""
from pathlib import Path
import os
import subprocess
import tempfile
ROOT=Path(__file__).resolve().parents[1]
def main():
    with tempfile.TemporaryDirectory() as temp:
        backend=Path(temp)/'ipl_backend'
        subprocess.run([os.environ.get('CC','cc'),'-std=c11','-O1','-Wall','-Wextra','-Werror','-Itests/fake_ipl','-Iinclude',
            'tests/ipl_backend_test.c','source/ipl_player_gamecube.c','-o',str(backend)],cwd=ROOT,check=True)
        subprocess.run([str(backend)],cwd=ROOT,check=True)
        for name,sources in (
            ('ipl_sequence',['ipl_sequence']),
            ('ipl_mixer',['ipl_sequence','ipl_mixer']),
            ('ipl_audio',['audio','ipl_sequence','ipl_mixer','ipl_audio']),
            ('ipl_player',['audio','ipl_sequence','ipl_mixer','ipl_audio','ipl_player'])):
            out=Path(temp)/name
            subprocess.run([os.environ.get('CC','cc'),'-D_DARWIN_C_SOURCE','-std=c11','-O1','-Wall','-Wextra','-Werror',
                '-Wno-unused-function','-Iinclude','tests/'+name+'_test.c',
                *['source/'+s+'.c' for s in sources],'-lm','-pthread','-o',str(out)],cwd=ROOT,check=True)
            subprocess.run([str(out)],cwd=ROOT,check=True)
if __name__=='__main__':main()
