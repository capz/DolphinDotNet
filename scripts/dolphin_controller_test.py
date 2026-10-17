#!/usr/bin/env python3
"""Inject real Dolphin pipe controllers and check the managed PAD acceptance DOL."""
import argparse
import json
import os
from pathlib import Path
import signal
import socket
import struct
import subprocess
import time

from dolphin_gdb_probe import _send, _reply, parse_stop_pc
from dolphin_smoke import check

BUTTONS = ('A','B','X','Y','Z','START','L','R','D_UP','D_DOWN','D_LEFT','D_RIGHT')
CHORD = ('A','B','X','Y','Z','START')


def cases():
    yield dict(name='initial neutral', port=0)
    for port in range(4):
        for button in BUTTONS:
            yield dict(name=f'port {port+1} {button} press',port=port,buttons=[button])
            yield dict(name=f'port {port+1} {button} hold',port=port,buttons=[button])
            yield dict(name=f'port {port+1} {button} release',port=port)
        yield dict(name=f'port {port+1} simultaneous buttons',port=port,buttons=CHORD)
        yield dict(name=f'port {port+1} simultaneous release',port=port)
        for axis in range(4):
            for value in (-1,-.5,0,.5,1):
                yield dict(name=f'port {port+1} axis {axis} value {value}',port=port,axis=axis,value=value)
        for axis in (4,5):
            for value in (0,.25,.5,.75,1):
                yield dict(name=f'port {port+1} trigger {axis-4} value {value}',port=port,axis=axis,value=value)
        yield dict(name=f'port {port+1} diagonal sticks',port=port,axis=6,value=1)
        yield dict(name=f'port {port+1} stick deadzones',port=port,axis=7,value=.04)
        yield dict(name=f'port {port+1} final neutral',port=port)


def commands(case, port):
    buttons=case.get('buttons',()) if case['port']==port else ()
    axes=[.5,.5,.5,.5,0,0]
    axis=case.get('axis',-1)
    if case['port']==port:
        if 0<=axis<4: axes[axis]=(case['value']+1)/2
        elif axis in (4,5): axes[axis]=case['value']
        elif axis==6: axes[:4]=[1,1,1,1]
        elif axis==7: axes[:4]=[.52,.52,.52,.52]
    return ''.join(f'{"PRESS" if b in buttons else "RELEASE"} {b}\n' for b in BUTTONS)+\
        f'SET MAIN {axes[0]} {axes[1]}\nSET C {axes[2]} {axes[3]}\nSET L {axes[4]}\nSET R {axes[5]}\n'


def pad_config():
    text=''
    for p in range(4):
        text+=f'[GCPad{p+1}]\nDevice = Pipe/0/dnd{p}\nOptions/Always Connected = True\n'
        for button in BUTTONS[:6]:
            text+=f'Buttons/{"Start" if button=="START" else button} = `Button {button}`\n'
        for group,pipe in [('Main Stick','MAIN'),('C-Stick','C')]:
            for direction,axis,sign in [('Up','Y','+'),('Down','Y','-'),('Left','X','-'),('Right','X','+')]:
                text+=f'{group}/{direction} = `Axis {pipe} {axis} {sign}`\n'
            text+=f'{group}/Dead Zone = 0.0\n'
        for button in ('L','R'):
            text+=f'Triggers/{button} = `Button {button}`\nTriggers/{button}-Analog = `Axis {button} +`\n'
        text+='Triggers/Dead Zone = 0.0\n'
        for direction in ('Up','Down','Left','Right'):
            text+=f'D-Pad/{direction} = `Button D_{direction.upper()}`\n'
        text+='\n'
    return text


def symbols(elf):
    names=('dnd_input_test_case','dnd_input_test_done','dnd_input_test_case_index','dnd_input_test_result','dnd_input_test_snapshot')
    output=subprocess.check_output(['readelf','-sW',str(elf)],text=True)
    found={}
    for line in output.splitlines():
        parts=line.split()
        if len(parts)>=8 and parts[7] in names:
            if parts[7] in found: raise ValueError('Duplicate symbol '+parts[7])
            found[parts[7]]=int(parts[1],16)
    if set(found)!=set(names): raise ValueError('Missing controller fixture symbols')
    return found


def request(conn,command,seconds=10):
    _send(conn,command)
    deadline=time.monotonic()+seconds
    reply=_reply(conn,deadline)
    # Dolphin may send an extra empty reply after its raw interrupt stop.
    while reply=='' or (command not in ('c','s') and reply.startswith('T05')):
        reply=_reply(conn,deadline)
    return reply


def memory(conn,address,length):
    data=bytes.fromhex(request(conn,f'm{address:x},{length:x}'))
    if len(data)!=length: raise ValueError('Truncated guest memory')
    return data


def snapshot(conn,address):
    raw=memory(conn,address,144)
    return [struct.unpack_from('>HHH2xi6f',raw,p*36) for p in range(4)]


def run(args, wrong_button=False):
    points=symbols(args.elf)
    root=args.logs.resolve();root.mkdir(parents=True,exist_ok=True)
    user=root/'user';config=user/'Config';config.mkdir(parents=True,exist_ok=True)
    pipes=user/'Pipes';pipes.mkdir(exist_ok=True)
    fds=[];process=None;conn=None;results=[]
    with socket.socket() as available:
        available.bind(('127.0.0.1',0));port=available.getsockname()[1]
    (config/'Dolphin.ini').write_text('[Core]\nGFXBackend = Null\nCPUCore = 1\nCPUThread = False\n'+
        ''.join(f'SIDevice{p} = 6\n' for p in range(4))+f'[General]\nGDBPort = {port}\n[DSP]\nBackend = No Audio Output\n[Interface]\nDebugModeEnabled = True\n')
    (config/'GCPadNew.ini').write_text(pad_config())
    (config/'Logger.ini').write_text('[Options]\nWriteToFile = True\nWriteToConsole = True\nVerbosity = 3\n[Logs]\nBOOT = True\nCORE = True\nPOWERPC = True\nOSREPORT = True\n')
    env=dict(os.environ)
    # Confine all emulator state to this test's isolated directory.
    home=root/'home';home.mkdir(exist_ok=True)
    env.update(HOME=str(home),DOLPHIN_EMU_USERPATH=str(user),SDL_AUDIODRIVER='dummy')
    for key,folder in [('XDG_CONFIG_HOME','config'),('XDG_CACHE_HOME','cache'),('XDG_DATA_HOME','data')]:
        path=home/folder;path.mkdir(exist_ok=True);env[key]=str(path)
    started=time.monotonic()
    try:
        for p in range(4):
            path=pipes/f'dnd{p}'
            if path.exists(): path.unlink()
            os.mkfifo(path)
            fds.append(os.open(path,os.O_RDWR|os.O_NONBLOCK))
            os.write(fds[-1],commands(dict(port=0),p).encode())
        with (root/'emulator.log').open('w') as log:
            process=subprocess.Popen(['xvfb-run','-a',args.binary,'--exec',str(args.dol.resolve())],env=env,stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
            while time.monotonic()-started<20:
                if process.poll() is not None: raise RuntimeError('Dolphin exited before GDB attach')
                try: conn=socket.create_connection(('127.0.0.1',port),timeout=.4);break
                except OSError: time.sleep(.1)
            if conn is None: raise TimeoutError('Dolphin GDB listener unavailable')
            conn.setsockopt(socket.IPPROTO_TCP,socket.TCP_NODELAY,1)
            for key in ('dnd_input_test_case','dnd_input_test_done'):
                if request(conn,f'Z0,{points[key]:x},4')!='OK': raise RuntimeError('Breakpoint refused')
            sequence=list(cases())
            for index,case in enumerate(sequence):
                if time.monotonic()-started>args.seconds: raise TimeoutError('Controller test total deadline exceeded')
                try:
                    pc=parse_stop_pc(request(conn,'c',40 if index==0 else 10))
                except TimeoutError:
                    conn.sendall(b'\x03')
                    stopped=_reply(conn,time.monotonic()+3)
                    result=memory(conn,points['dnd_input_test_result'],4).hex()
                    raise TimeoutError(f'Case {index} ({case["name"]}) stalled: {stopped}; result={result}')
                if pc!=points['dnd_input_test_case']:
                    result=int.from_bytes(memory(conn,points['dnd_input_test_result'],4),'big')
                    pads=snapshot(conn,points['dnd_input_test_snapshot'])
                    raise AssertionError(f'Guest ended before case {index} ({case["name"]}); result=0x{result:08x}, pads [held,pressed,released,flags,axes]={pads}')
                actual=int.from_bytes(memory(conn,points['dnd_input_test_case_index'],4),'big')
                if actual!=index: raise AssertionError(f'Case order: {actual}, expected {index}')
                # Reaching the next case proves the managed assertions for the previous one passed.
                if index: results.append(sequence[index-1]['name'])
                if index%25==0: print(f'Case {index+1}/{len(sequence)}: {case["name"]}',flush=True)
                injected=dict(case,buttons=['B']) if wrong_button and index==1 else case
                for p,fd in enumerate(fds):
                    data=commands(injected,p).encode()
                    if os.write(fd,data)!=len(data): raise RuntimeError('Partial pipe write')
                if request(conn,f'z0,{points["dnd_input_test_case"]:x},4')!='OK': raise RuntimeError('Breakpoint removal refused')
                request(conn,'s')
                if request(conn,f'Z0,{points["dnd_input_test_case"]:x},4')!='OK': raise RuntimeError('Breakpoint reinstall refused')
            pc=parse_stop_pc(request(conn,'c'))
            result=int.from_bytes(memory(conn,points['dnd_input_test_result'],4),'big')
            if pc!=points['dnd_input_test_done'] or result!=0x50414453:
                raise AssertionError(f'Guest did not finish successfully: PC=0x{pc:08x}, result=0x{result:08x}')
            results.append(sequence[-1]['name'])
    finally:
        if conn: conn.close()
        if process and process.poll() is None:
            os.killpg(process.pid,signal.SIGTERM)
            try: process.wait(timeout=5)
            except subprocess.TimeoutExpired: pass
        # xvfb-run can exit before Dolphin has handled SIGTERM. Reap the entire
        # test-owned group even when its shell has already exited.
        if process:
            try: os.killpg(process.pid,signal.SIGKILL)
            except ProcessLookupError: pass
            process.wait()
        for fd in fds: os.close(fd)
        (root/'results.json').write_text(json.dumps(dict(passed=len(results),cases=results),indent=2)+'\n')
    faults=check((root/'emulator.log').read_text(errors='replace'))
    if faults: raise AssertionError('Emulator faults: '+str(faults))
    print(f'PASS: {len(results)} managed controller cases across all four ports ({time.monotonic()-started:.1f}s).')


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('dol',type=Path);parser.add_argument('--elf',type=Path,required=True)
    parser.add_argument('--binary',default='/usr/games/dolphin-emu-nogui')
    parser.add_argument('--logs',type=Path,default=Path('dolphin-controller'))
    parser.add_argument('--seconds',type=int,default=240)
    parser.add_argument('--verify-rejection',action='store_true',help='Also prove a deliberately wrong button fails inside managed code')
    args=parser.parse_args()
    if args.verify_rejection:
        verify_rejection(args)
    run(args)


def verify_rejection(args):
    negative=argparse.Namespace(**vars(args));negative.logs=args.logs/'negative-control'
    try: run(negative,wrong_button=True)
    except AssertionError as error:
        # Managed case 2, error 7: B was held where A was required. Startup,
        # timing and unrelated failures must not count as a passing control.
        if 'result=0xdead00cf' not in str(error): raise
        print('PASS: managed assertions rejected deliberately swapped A/B input.',flush=True)
    else: raise AssertionError('Deliberately wrong input was accepted')


if __name__=='__main__': main()
