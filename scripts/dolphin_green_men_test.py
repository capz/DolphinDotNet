#!/usr/bin/env python3
"""Exercise the actual Green Men DOL using Dolphin pipe inputs and read-only GDB observations."""
import argparse
import json
import os
from pathlib import Path
import signal
import socket
import struct
import subprocess
import time

from dolphin_controller_test import commands, pad_config, request, memory
from dolphin_gdb_probe import parse_stop_pc, resolve_symbols
from dolphin_smoke import check


def run(args):
    root=args.logs.resolve();root.mkdir(parents=True,exist_ok=True)
    config=root/'user'/'Config';config.mkdir(parents=True,exist_ok=True)
    pipes=root/'user'/'Pipes';pipes.mkdir(exist_ok=True)
    model=json.loads(args.source_map.read_text())['inspection']
    game_type=next(t for t in model['types'] if t['name']=='GreenMenGame')
    if model['pointerSize']!=4 or not 8<=game_type['size']<=4096:raise ValueError('Unsupported game layout')
    fields={f['name']:f for f in game_type['fields']}
    functions=dict(resolve_symbols(args.elf,['dnd_value_GreenMen_GreenMenGame_Update_*','dnd_device_end']))
    update=next(v for k,v in functions.items() if k!='dnd_device_end');frame=functions['dnd_device_end']
    with socket.socket() as available:
        available.bind(('127.0.0.1',0));port=available.getsockname()[1]
    (config/'Dolphin.ini').write_text('[Core]\nGFXBackend = Null\nCPUCore = 1\nCPUThread = False\nSIDevice0 = 6\n'+
        f'[General]\nGDBPort = {port}\n[DSP]\nBackend = No Audio Output\n[Interface]\nDebugModeEnabled = True\n')
    (config/'GCPadNew.ini').write_text(pad_config())
    (config/'Logger.ini').write_text('[Options]\nWriteToFile = True\nWriteToConsole = True\nVerbosity = 3\n[Logs]\nBOOT = True\nCORE = True\nPOWERPC = True\n')
    environment=dict(os.environ)
    environment.update(HOME=str(root),DOLPHIN_EMU_USERPATH=str(root/'user'),SDL_AUDIODRIVER='dummy')
    for key,name in [('XDG_CONFIG_HOME','config'),('XDG_CACHE_HOME','cache'),('XDG_DATA_HOME','data')]:
        path=root/name;path.mkdir(exist_ok=True);environment[key]=str(path)
    process=None;conn=None;fd=None;proof=[];started=time.monotonic()
    try:
        pipe=pipes/'dnd0'
        if pipe.exists():pipe.unlink()
        os.mkfifo(pipe);fd=os.open(pipe,os.O_RDWR|os.O_NONBLOCK)
        os.write(fd,commands(dict(port=0),0).encode())
        with (root/'emulator.log').open('w') as log:
            process=subprocess.Popen(['xvfb-run','-a',args.binary,'--exec',str(args.dol.resolve())],env=environment,
                                     stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
            while time.monotonic()-started<15:
                if process.poll() is not None:raise RuntimeError('Dolphin exited before attach')
                try:conn=socket.create_connection(('127.0.0.1',port),timeout=.4);break
                except OSError:time.sleep(.1)
            if conn is None:raise TimeoutError('No Dolphin GDB listener')
            conn.setsockopt(socket.IPPROTO_TCP,socket.TCP_NODELAY,1)
            for address in (update,frame):
                if request(conn,f'Z0,{address:x},4')!='OK':raise RuntimeError('Breakpoint refused')
            def observe(buttons=(),frames=8):
                os.write(fd,commands(dict(port=0,buttons=buttons),0).encode())
                state=None
                for _ in range(frames):
                    if time.monotonic()-started>args.seconds:raise TimeoutError('Game acceptance deadline exceeded')
                    stop=parse_stop_pc(request(conn,'c'))
                    if stop==update:
                        registers=bytes.fromhex(request(conn,'g'))
                        game=int.from_bytes(registers[12:16],'big') # PowerPC r3 is managed this.
                        if not 0x80000000<=game<0x81800000:raise ValueError('Invalid managed game pointer')
                        data=memory(conn,game,game_type['size'])
                        state={}
                        for name in ['State','PlayerX','Lives','Score','Wave','FormationX','FormationY','ShotY','ShotActive']:
                            field=fields.get(name) or fields[f'<{name}>k__BackingField']
                            offset=field['offset'];size=field['storageBytes']
                            state[name]=struct.unpack_from('>f',data,offset)[0] if field['type']=='System.Single' else int.from_bytes(data[offset:offset+size],'big')
                        stop=parse_stop_pc(request(conn,'c'))
                    if stop!=frame:raise ValueError(f'Unexpected game stop {stop:08x}')
                if state is None:raise AssertionError('No connected controller/game update observed')
                return state
            def require(value,label):
                if not value:raise AssertionError(label)
                proof.append(label)
            initial=observe()
            require(initial['State']==0,'title renders with connected controller')
            playing=observe(['A'])
            require(playing['State']==1 and playing['Lives']==3 and playing['Wave']==1,'A starts a three-life game')
            require(playing['ShotActive'] or playing['Score']>0,'held A fires a real projectile')
            observe()
            moved=observe(['D_LEFT'])
            require(moved['PlayerX']<playing['PlayerX'],'D-pad moves the ship left')
            observe()
            paused=observe(['START']);observe()
            frozen=observe(['D_RIGHT','A'])
            require(paused['State']==2 and frozen['State']==2,'Start pauses')
            require(all(paused[k]==frozen[k] for k in ('PlayerX','FormationX','FormationY','ShotY')),'pause freezes ship, invaders and projectiles')
            observe();resumed=observe(['START'])
            require(resumed['State']==1,'Start resumes')
            observe();title=observe(['B'])
            require(title['State']==0,'B returns to title')
            observe();restarted=observe(['A'])
            require(restarted['State']==1 and restarted['Score']==0 and restarted['Lives']==3,'new game resets score and lives')
        return proof
    finally:
        if conn:conn.close()
        if fd is not None:os.close(fd)
        if process and process.poll() is None:
            os.killpg(process.pid,signal.SIGTERM)
            try:process.wait(timeout=3)
            except subprocess.TimeoutExpired:os.killpg(process.pid,signal.SIGKILL);process.wait(timeout=3)
        log_path=root/'emulator.log'
        if log_path.exists():
            errors=check(log_path.read_text(errors='replace'))
            if errors:raise AssertionError('\n'.join(errors))


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('dol',type=Path);parser.add_argument('--elf',type=Path,required=True)
    parser.add_argument('--source-map',type=Path,required=True)
    parser.add_argument('--logs',type=Path,default=Path('dolphin-green-men'))
    parser.add_argument('--binary',default='/usr/games/dolphin-emu-nogui')
    parser.add_argument('--seconds',type=int,default=60)
    args=parser.parse_args()
    proof=run(args)
    (args.logs/'results.json').write_text(json.dumps(dict(passed=True,checks=proof),indent=2)+'\n')
    print('Green Men real Dolphin inputs passed: '+', '.join(proof))
    return 0


if __name__=='__main__':raise SystemExit(main())
