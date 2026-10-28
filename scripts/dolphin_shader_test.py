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

CAPTURE_TIMEOUT_SECONDS=90
FFMPEG_TIMEOUT_SECONDS=30


def wait_for_capture(process,frames,seconds):
    deadline=time.monotonic()+seconds
    while time.monotonic()<deadline:
        if process.poll() is not None:raise RuntimeError('Dolphin exited before rendering')
        captures=sorted(frames.glob('*.avi'))
        for capture in captures:
            if capture.stat().st_size>256*1024:return capture
        time.sleep(.05)
    sizes={p.name:p.stat().st_size for p in frames.glob('*.avi')}
    raise TimeoutError(f'No rendered shader frames within {seconds}s; capture files: {sizes}')


def run_ffmpeg(arguments,log_path,*,timeout=FFMPEG_TIMEOUT_SECONDS,**kwargs):
    # Bound decoder, filter and encoder pools separately. Auto-sizing against
    # host CPU count oversubscribes constrained CI runners (CI 1363).
    command=['ffmpeg','-hide_banner','-loglevel','error','-nostdin',
             '-threads','1','-filter_threads','1','-filter_complex_threads','1',
             *arguments[:-1],'-threads','1',arguments[-1]]
    try:
        result=subprocess.run(command,check=True,stdout=subprocess.PIPE,
                              stderr=subprocess.PIPE,timeout=timeout,**kwargs)
    except (subprocess.TimeoutExpired,subprocess.CalledProcessError) as error:
        diagnostics=(error.stderr or b'').decode(errors='replace')
        log_path.write_text(diagnostics)
        raise RuntimeError(f'FFmpeg failed ({error}); diagnostics: {log_path}\n{diagnostics[-4000:]}') from error
    log_path.write_bytes(result.stderr)
    return result.stdout


def decode_capture(capture,log_path):
    # Normalize PAL/NTSC output to the sample's logical coordinates.
    raw=run_ffmpeg(['-i',str(capture),'-vf','scale=640:480:flags=neighbor',
                    '-frames:v','6','-f','rawvideo','-pix_fmt','rgb24','pipe:1'],log_path)
    size=640*480*3
    if len(raw)<2*size or len(raw)%size:
        raise ValueError(f'Incomplete capture: {len(raw)} RGB bytes; need at least two complete frames')
    return [raw[i:i+size] for i in range(0,len(raw),size)]


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
    # The disk uses the managed toon preset; the strip uses the compiled graph.
    for y,band in ((240,2),(275,1),(320,0)):
        actual=pixel(528,y)
        if max(abs(actual[c]-colors[band][c]) for c in range(3))>12:
            raise AssertionError(f'Managed toon preset differs at {y}: {actual}')
    hud=pixel(33,411)
    if max(abs(hud[c]-(231,239,252)[c]) for c in range(3))>12:
        raise AssertionError(f'Default HUD pipeline was not restored: {hud}')
    ramp=pixel(320)
    if not ramp[0]+35<ramp[1]<ramp[2] or ramp[2]<115:
        raise AssertionError(f'TEV color ramp differs: {ramp}')
    presets=validate_presets(rgb)
    return dict(reference=pixel(112),ramp=ramp,toonBands=sorted(levels),presets=presets)


def validate_presets(rgb):
    # Auxiliary maps have alpha zero. A wrong final texture/alpha stage makes
    # these patches disappear, even if RGB stage arithmetic is otherwise right.
    checks=((60,(40,60,80)),(140,(140,100,100)),(220,(100,160,220)),
            (300,(80,120,160)),(380,(220,40,20)),(480,(40,80,120)),(560,(40,80,120)))
    proof=[]
    for x,expected in checks:
        at=(362*640+x)*3;actual=tuple(rgb[at:at+3])
        if max(abs(actual[c]-expected[c]) for c in range(3))>12:
            raise AssertionError(f'Material preset/override differs at {x}: {actual}, expected {expected}')
        proof.append(actual)
    return proof


def validate_model(rgb,effect=True):
    """Imported textured cutout and untextured material, with per-draw defaults."""
    if len(rgb)!=640*480*3:raise ValueError('Incomplete model frame')
    def pixel(x):
        at=(116*640+x)*3
        return tuple(rgb[at:at+3])
    def close(actual,expected,label):
        if max(abs(a-b) for a,b in zip(actual,expected))>12:
            raise AssertionError(f'{label}: {actual}, expected {expected}')
    for shift in (0,208,416):
        close(pixel(40+shift),(8,13,26),'Imported texture alpha cutout lost')
        custom=effect and shift==208
        close(pixel(80+shift),(70,220,248) if custom else (204,204,204),'Imported textured mesh combiner')
        close(pixel(140+shift),(30,90,158) if custom else (102,102,102),'Imported untextured mesh combiner')
    return dict(textured=pixel(288),untextured=pixel(348),cutout=pixel(248))


def run(args,imported_models=False):
    root=args.logs.resolve()
    if root.exists() and any(root.iterdir()):raise ValueError('Use a fresh capture directory')
    config=root/'user'/'Config';config.mkdir(parents=True,exist_ok=True)
    (config/'Dolphin.ini').write_text('[Core]\nGFXBackend = Software\nCPUCore = 1\nCPUThread = False\n'+
        '[DSP]\nBackend = No Audio Output\n[Movie]\nDumpFrames = True\nDumpFramesSilent = True\n')
    (config/'GFX.ini').write_text('[Settings]\nDumpFormat = avi\nDumpCodec = ffv1\nDumpEncoder = ffv1\nDumpPixelFormat = bgr0\n')
    (config/'Logger.ini').write_text('[Options]\nWriteToFile = True\nWriteToConsole = True\nVerbosity = 3\n[Logs]\nBOOT = True\nCORE = True\nPOWERPC = True\n')
    environment=dict(os.environ)
    # Mesa also presents the software backend's output. Its host-sized worker
    # pool can oversubscribe a small CI CPU quota during startup.
    environment.update(DOLPHIN_EMU_USERPATH=str(root/'user'),
                       SDL_AUDIODRIVER='dummy',LP_NUM_THREADS='2')
    for key,name in [('XDG_CONFIG_HOME','config'),('XDG_CACHE_HOME','cache'),('XDG_DATA_HOME','data')]:
        path=root/name;path.mkdir(exist_ok=True);environment[key]=str(path)
    started=time.monotonic();process=None;capture=None
    try:
        with (root/'emulator.log').open('w') as log:
            process=subprocess.Popen(['xvfb-run','-a',args.binary,'--user',str(root/'user'),
                                      '--exec',str(args.dol.resolve())],
                env=environment,stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
            try:
                capture=wait_for_capture(process,root/'user'/'Dump'/'Frames',args.seconds)
            except (RuntimeError,TimeoutError) as error:
                log.flush()
                diagnostics=(root/'emulator.log').read_text(errors='replace')
                raise RuntimeError(f'{error}\nDolphin log ({root}/emulator.log):\n{diagnostics[-8000:]}') from error
    finally:
        if process is not None and process.poll() is None:
            os.killpg(process.pid,signal.SIGTERM)
            try:process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                os.killpg(process.pid,signal.SIGKILL);process.wait(timeout=5)
        if process is not None:
            try:os.killpg(process.pid,signal.SIGKILL)
            except ProcessLookupError:pass
    diagnostics=(root/'emulator.log').read_text(errors='replace')
    for path in (root/'user'/'Logs').glob('*.log'):
        diagnostics+=path.read_text(errors='replace')
    errors=check(diagnostics)
    if errors:raise RuntimeError('\n'.join(errors))
    frames=decode_capture(capture,root/'decode.log')
    proof=None
    if not imported_models:
        proof=validate(frames[1])
        selected=frames[1]
    # Allow the initial video clear before the first managed frame. Require a
    # complete default/effect/default cycle, including unchanged public mesh/UI.
    for i in range(len(frames)-2) if imported_models else ():
        try:
            for j,custom in enumerate((False,True,False)):
                validate(frames[i+j]);validate_model(frames[i+j],custom)
        except (AssertionError,ValueError):continue
        proof=validate(frames[i+1]);proof['importedModel']=validate_model(frames[i+1])
        proof['defaultEffectDefaultFrames']=[i,i+1,i+2]
        selected=frames[i+1]
        break
    if proof is None:
        raise AssertionError('No valid imported-model default/effect/default cycle in first six frames')
    run_ffmpeg(['-f','rawvideo','-pix_fmt','rgb24','-s','640x480',
                '-i','pipe:0','-frames:v','1','-y',str(root/'frame.png')],
               root/'encode.log',input=selected)
    proof['elapsedSeconds']=round(time.monotonic()-started,3)
    (root/'results.json').write_text(json.dumps(proof,indent=2)+'\n')
    print(json.dumps(proof))


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('dol',type=Path)
    parser.add_argument('--binary',default='/usr/games/dolphin-emu-nogui')
    parser.add_argument('--logs',type=Path,default=Path('dolphin-shader'))
    parser.add_argument('--seconds',type=float,default=CAPTURE_TIMEOUT_SECONDS)
    run(parser.parse_args(),imported_models=True)
