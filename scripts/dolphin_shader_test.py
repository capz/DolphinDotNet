#!/usr/bin/env python3
"""Validate real TEV output from a short software-rendered Dolphin capture.

The gradient strips supply the same interpolated grayscale input to each
program. Pixel checks detect a missing/wrong combiner stage, not just boot.
"""
import argparse
import json
import os
from pathlib import Path
import signal
import subprocess
import time
from dolphin_smoke import check


def validate(rgb):
    if len(rgb)!=640*480*3:
        raise ValueError('Incomplete RGB frame')
    def pixel(x,y=347):
        at=(y*640+x)*3
        return tuple(rgb[at:at+3])
    levels=set()
    colors=((12,24,64),(30,90,158),(70,220,248))
    for x in range(40,187,5):
        reference=pixel(x)
        expected=255*(x-34)/156
        if max(reference)-min(reference)>8 or abs(reference[0]-expected)>12:
            raise AssertionError(f'Reference raster gradient differs at {x}: {reference}')
    # Interior samples remain in the same band across the full animated
    # threshold range. Avoid XFB chroma sharing at a moving band's edge.
    for band,positions in enumerate(((40,50,60),(104,112,120),(172,180,186))):
        for x in positions:
            toon=pixel(x+416)
            if max(abs(toon[c]-colors[band][c]) for c in range(3))>12:
                raise AssertionError(f'TEV toon band {band} differs at {x}: {toon}')
            levels.add(band)
    if levels!={0,1,2}:
        raise AssertionError(f'TEV did not produce all three bands: {levels}')
    ramp=pixel(320)
    if not ramp[0]+35<ramp[1]<ramp[2] or ramp[2]<115:
        raise AssertionError(f'TEV color ramp differs: {ramp}')
    return dict(reference=pixel(112),ramp=ramp,toonBands=sorted(levels))


def run(args):
    root=args.logs.resolve()
    if root.exists() and any(root.iterdir()):raise ValueError('Use a fresh capture directory')
    config=root/'user'/'Config';config.mkdir(parents=True,exist_ok=True)
    (config/'Dolphin.ini').write_text('[Core]\nGFXBackend = Software\nCPUCore = 1\nCPUThread = False\n'+
        '[DSP]\nBackend = No Audio Output\n[Movie]\nDumpFrames = True\nDumpFramesSilent = True\n')
    (config/'GFX.ini').write_text('[Settings]\nDumpFormat = avi\nDumpCodec = ffv1\nDumpEncoder = ffv1\nDumpPixelFormat = bgr0\n')
    (config/'Logger.ini').write_text('[Options]\nWriteToFile = True\nWriteToConsole = True\nVerbosity = 3\n[Logs]\nBOOT = True\nCORE = True\nPOWERPC = True\n')
    environment=dict(os.environ)
    environment.update(HOME=str(root),DOLPHIN_EMU_USERPATH=str(root/'user'),SDL_AUDIODRIVER='dummy')
    for key,name in [('XDG_CONFIG_HOME','config'),('XDG_CACHE_HOME','cache'),('XDG_DATA_HOME','data')]:
        path=root/name;path.mkdir(exist_ok=True);environment[key]=str(path)
    started=time.monotonic();process=None;capture=None
    try:
        with (root/'emulator.log').open('w') as log:
            process=subprocess.Popen(['xvfb-run','-a',args.binary,'--exec',str(args.dol.resolve())],
                env=environment,stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
            while time.monotonic()-started<args.seconds:
                if process.poll() is not None:raise RuntimeError('Dolphin exited before rendering')
                captures=list((root/'user'/'Dump'/'Frames').glob('*.avi'))
                if captures and captures[0].stat().st_size>256*1024:
                    capture=captures[0];break
                time.sleep(.05)
            if capture is None:raise TimeoutError('No rendered shader frames before deadline')
    finally:
        if process is not None and process.poll() is None:
            os.killpg(process.pid,signal.SIGTERM)
            try:process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                os.killpg(process.pid,signal.SIGKILL);process.wait(timeout=5)
    diagnostics=(root/'emulator.log').read_text(errors='replace')
    for path in (root/'user'/'Logs').glob('*.log'):
        diagnostics+=path.read_text(errors='replace')
    errors=check(diagnostics)
    if errors:raise RuntimeError('\n'.join(errors))
    # Normalize PAL/NTSC output to the sample's logical coordinates.
    command=['ffmpeg','-hide_banner','-loglevel','error','-i',str(capture),
             '-vf','select=eq(n\\,1),scale=640:480:flags=neighbor','-frames:v','1']
    rgb=subprocess.run(command+['-f','rawvideo','-pix_fmt','rgb24','pipe:1'],
                       check=True,stdout=subprocess.PIPE,timeout=10).stdout
    proof=validate(rgb)
    subprocess.run(command+['-y',str(root/'frame.png')],check=True,timeout=10)
    proof['elapsedSeconds']=round(time.monotonic()-started,3)
    (root/'results.json').write_text(json.dumps(proof,indent=2)+'\n')
    print(json.dumps(proof))


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('dol',type=Path)
    parser.add_argument('--binary',default='/usr/games/dolphin-emu-nogui')
    parser.add_argument('--logs',type=Path,default=Path('dolphin-shader'))
    parser.add_argument('--seconds',type=float,default=30)
    run(parser.parse_args())
