#!/usr/bin/env python3
"""Drive the real DiscBrowser sample through Dolphin pipe controllers and GDB."""
import argparse
import json
import os
from pathlib import Path
import signal
import socket
import subprocess
import time
from dolphin_controller_test import commands, memory, pad_config, request
from dolphin_gdb_probe import parse_stop_pc
from dolphin_smoke import check


def run(args):
    root=args.logs.resolve()
    if root.exists() and any(root.iterdir()):raise ValueError('Use a fresh log directory')
    user=root/'user';config=user/'Config';config.mkdir(parents=True)
    pipes=user/'Pipes';pipes.mkdir()
    with socket.socket() as available:
        available.bind(('127.0.0.1',0));port=available.getsockname()[1]
    (config/'Dolphin.ini').write_text('[Core]\nGFXBackend = Null\nCPUCore = 1\nCPUThread = False\n'+
        ''.join(f'SIDevice{p} = 6\n' for p in range(4))+f'DefaultISO = {args.disc.resolve()}\n'+
        f'[General]\nGDBPort = {port}\n[DSP]\nBackend = No Audio Output\n[Interface]\nDebugModeEnabled = True\n')
    (config/'GCPadNew.ini').write_text(pad_config())
    (config/'Logger.ini').write_text('[Options]\nWriteToFile = True\nWriteToConsole = True\nVerbosity = 3\n[Logs]\nBOOT = True\nCORE = True\nPOWERPC = True\n')
    env=dict(os.environ,DOLPHIN_EMU_USERPATH=str(user),SDL_AUDIODRIVER='dummy',LP_NUM_THREADS='2')
    for key,folder in [('XDG_CONFIG_HOME','config'),('XDG_CACHE_HOME','cache'),('XDG_DATA_HOME','data')]:
        path=root/folder;path.mkdir();env[key]=str(path)
    output=subprocess.check_output(['readelf','-sW',str(args.elf)],text=True)
    points={p[7]:int(p[1],16) for line in output.splitlines()
            if len(p:=line.split())>=8 and p[0].rstrip(':').isdigit()}
    frame=points['dnd_device_end'];finished=points['dnd_platform_wait_forever']
    fds=[];process=None;conn=None;results=[];started=time.monotonic()
    def number(name):return int.from_bytes(memory(conn,points['dnd_static_Program_'+name],4),'big')
    def current():
        address=number('current');length=int.from_bytes(memory(conn,address+8,4),'big')
        if length>1024:raise AssertionError('Invalid current directory length')
        return memory(conn,address+12,length*2).decode('utf-16-be') if length else ''
    def advance(case=None,frames=8,exit_expected=False):
        os.write(fds[0],commands(dict(port=0,**(case or {})),0).encode())
        for _ in range(frames):
            if time.monotonic()-started>args.seconds:raise TimeoutError('Browser input deadline exceeded')
            request(conn,f'z0,{frame:x},4');request(conn,'s');request(conn,f'Z0,{frame:x},4')
            pc=parse_stop_pc(request(conn,'c',15))
            if pc==finished:
                if exit_expected:return
                raise AssertionError('Browser exited unexpectedly')
            if pc!=frame:raise AssertionError(f'Unexpected stop: {pc:x}')
        if exit_expected:raise AssertionError('Start did not exit the browser')
    def require(condition,label):
        if not condition:raise AssertionError(label)
        results.append(label)
        print("PASS: "+label,flush=True)
    try:
        for p in range(4):
            path=pipes/f'dnd{p}';os.mkfifo(path);fd=os.open(path,os.O_RDWR|os.O_NONBLOCK);fds.append(fd)
            os.write(fd,commands(dict(port=0),p).encode())
        with (root/'emulator.log').open('w') as log:
            process=subprocess.Popen(['xvfb-run','-a',args.binary,'--user',str(user),'--exec',str(args.dol.resolve())],
                env=env,stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
            while time.monotonic()-started<20:
                if process.poll() is not None:raise RuntimeError('Dolphin exited during startup')
                try:conn=socket.create_connection(('127.0.0.1',port),timeout=.4);break
                except OSError:time.sleep(.1)
            if conn is None:raise TimeoutError('GDB listener unavailable')
            conn.setsockopt(socket.IPPROTO_TCP,socket.TCP_NODELAY,1)
            for address in (frame,finished):
                if request(conn,f'Z0,{address:x},4')!='OK':raise RuntimeError('Breakpoint refused')
            if parse_stop_pc(request(conn,'c',30))!=frame:raise AssertionError('Browser did not render')
            advance();require(number('disc')==0 and current()=='' and number('selection')==0,'no automatic DVD selection')
            advance({'buttons':['Y']});require(number('disc')==1 and current()=='' and number('selection')==0,'Y switches to mounted DVD')
            advance({'buttons':['D_DOWN']});require(number('selection')==1,'D-pad down')
            advance();advance({'buttons':['D_UP']});require(number('selection')==0,'D-pad up')
            advance();advance({'axis':1,'value':-1});require(number('selection')==1,'analog down')
            advance({'axis':1,'value':-1},75)
            require(number('selection')>=8 and number('top')>0,'held analog scrolls beyond first page')
            advance({'axis':1,'value':1},100);require(number('selection')==0 and number('top')==0,'analog up clamps at root list start')
            advance();advance({'buttons':['A']});require(current()=='dir','A opens selected directory')
            advance();advance({'buttons':['B']});require(current()=='','B returns to parent')
            advance();advance({'buttons':['D_DOWN']});require(number('selection')==1,'selection before refresh')
            advance();advance({'buttons':['X']});require(number('selection')==0 and current()=='','X refresh resets list')
            advance();advance({'buttons':['START']},exit_expected=True);results.append('Start exits')
    finally:
        if conn:conn.close()
        if process:
            if process.poll() is None:
                try:os.killpg(process.pid,signal.SIGTERM)
                except ProcessLookupError:pass
                try:process.wait(timeout=5)
                except subprocess.TimeoutExpired:pass
            try:os.killpg(process.pid,signal.SIGKILL)
            except ProcessLookupError:pass
            process.wait()
        for fd in fds:os.close(fd)
        (root/'results.json').write_text(json.dumps(dict(passed=len(results),checks=results),indent=2)+'\n')
    faults=check((root/'emulator.log').read_text(errors='replace'))
    if faults:raise AssertionError(str(faults))
    print(f'PASS: {len(results)} DiscBrowser input checks ({time.monotonic()-started:.1f}s)')


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('dol',type=Path);parser.add_argument('--elf',type=Path,required=True)
    parser.add_argument('--disc',type=Path,required=True,help='Synthetic many-files ISO from tests/make_disc_fixture.py')
    parser.add_argument('--binary',default='/usr/games/dolphin-emu-nogui')
    parser.add_argument('--logs',type=Path,default=Path('dolphin-disc-browser-input'))
    parser.add_argument('--seconds',type=float,default=120)
    run(parser.parse_args())
