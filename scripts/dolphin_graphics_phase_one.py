"""Visual acceptance for managed cameras, lights, fog, sampling, sprites and clips."""
import argparse
from pathlib import Path
from dolphin_texture_test import FRAME_BYTES,run
# Pixel interiors avoid raster and texture-edge rounding.
PATCHES=(('orthographic red',80,288,(220,40,40),8),
         # Lit base (117,7,4) plus independent blue emission (20,20,220).
         ('dual texture generated UV',320,188,(137,27,224),8),
         ('directional light',240,288,(136,96,56),14),
         ('repeated UV',400,288,(220,20,20),8),
         ('orthographic fog',560,288,(110,20,120),16),
         ('camera and nested clip',30,380,(200,40,200),8),
         ('clipped exterior',15,380,(0,0,0),8),
         ('flipped sprite left',180,370,(20,220,20),8),
         ('flipped sprite right',220,370,(220,20,20),8),
         ('logical viewport',450,400,(20,40,220),8),
         ('viewport exterior',550,400,(0,0,0),8),
         ('retained rounded rectangle',36,36,(20,200,120),8),
         ('retained circle',108,36,(200,40,160),8),
         ('retained rounded exterior',9,62,(0,0,0),8),
         ('cached aligned text',226,44,(240,240,40),8),
         ('cached alignment exterior',218,44,(0,0,0),8),
         ('captured TEV inversion',316,36,(235,55,135),12),
         ('billboard',384,40,(220,160,20),8),
         ('mipmapped minification',464,40,(20,20,220),16),
         ('retained gradient',176,36,(120,0,120),12),
         ('primitive box',544,40,(40,180,220),8),
         ('primitive sphere',608,40,(40,180,220),8))
def validate_frame(rgb):
    if len(rgb)!=FRAME_BYTES:raise ValueError('Incomplete RGB frame')
    for name,x,y,color,tolerance in PATCHES:
        at=(y*640+x)*3;actual=rgb[at:at+3]
        if max(abs(a-b) for a,b in zip(actual,color))>tolerance:
            raise AssertionError(f'{name}: pixel {x},{y} is {tuple(actual)}, expected {color}')
def validate_capture(raw):
    if len(raw)%FRAME_BYTES:raise ValueError('Incomplete capture')
    frames=len(raw)//FRAME_BYTES
    if frames<60:raise AssertionError('Insufficient sustained frames')
    for i in range(frames-30,frames):validate_frame(raw[i*FRAME_BYTES:(i+1)*FRAME_BYTES])
    rgb=raw[-FRAME_BYTES:];values=[]
    for field in range(8):
        value=0
        for bit in range(16):
            index=field*16+bit;at=((450+(index//64)*4)*640+(index%64)*10+5)*3
            pixel=rgb[at:at+3]
            if max(pixel)-min(pixel)>12:raise AssertionError('Corrupt benchmark telemetry')
            if sum(pixel)>384:value|=1<<bit
        values.append(value)
    if values[:2]!=[0x4750,1] or values[2]!=240 or values[3]!=64:raise AssertionError('Incomplete submission benchmark')
    # Comparable paired workloads in the same emulated frame. Keep a generous
    # 10% regression budget: this gates throughput without pinning host speed.
    timings=values[4:]
    if any(t<=0 or t>=65535 for t in timings):raise AssertionError('Invalid or saturated benchmark timings')
    for name,individual,bulk in (('mesh',*timings[:2]),('sprite',*timings[2:])):
        if bulk>individual*1.10:raise AssertionError(f'{name} bulk submission regressed: {bulk} > {individual} us + 10%')
    phase_two=[]
    for field in range(4):
        value=0
        for bit in range(16):
            at=(466*640+(field*16+bit)*10+5)*3
            pixel=rgb[at:at+3]
            if max(pixel)-min(pixel)>12:raise AssertionError('Corrupt phase-two telemetry')
            if sum(pixel)>384:value|=1<<bit
        phase_two.append(value)
    if phase_two[:2]!=[0x4752,240]:raise AssertionError('Incomplete cached-layout benchmark')
    immediate,cached=phase_two[2:]
    if min(immediate,cached)<=0 or max(immediate,cached)>=65535:raise AssertionError('Invalid layout timings')
    if cached>immediate*1.10:raise AssertionError('Cached text submission regressed beyond 10%')
    return dict(phaseTwoGraphics=True,cachedTextBenchmark={"samples":240,"glyphs":4,"immediateUs":immediate,"cachedUs":cached},phaseOneGraphics=True,renderedFrames=frames,validatedTailFrames=30,patches=len(PATCHES),submissionBenchmark={'samples':values[2],'items':values[3],'meshIndividualUs':values[4],'meshBulkUs':values[5],'spriteIndividualUs':values[6],'spriteBulkUs':values[7]})
if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('dol',type=Path);p.add_argument('--logs',type=Path,required=True);p.add_argument('--seconds',type=float,default=20);p.add_argument('--backend',choices=['OGL','Software'],default='OGL')
    a=p.parse_args();a.streaming=a.benchmark=False;a.mode='pages';a.binary='/usr/games/dolphin-emu-nogui'
    if not 5<=a.seconds<=120:p.error('Require 5–120 seconds')
    run(a,validate_capture)
