"""RGBA8/CMPR pixel consistency under repeated detached resolution swaps."""
import argparse
from pathlib import Path
from dolphin_texture_test import FRAME_BYTES,run
def validate_capture(raw):
    if len(raw)%FRAME_BYTES or len(raw)//FRAME_BYTES<60:raise AssertionError('Insufficient complete frames')
    publications=[]
    for start in range(len(raw)-30*FRAME_BYTES,len(raw),FRAME_BYTES):
        rgb=raw[start:start+FRAME_BYTES]
        for left in (32,352):
            for dx,dy,color in ((64,64,(255,0,0)),(192,64,(0,255,0)),(64,192,(255,0,255)),(192,192,(0,255,255))):
                at=((80+dy)*640+left+dx)*3
                if max(abs(a-b) for a,b in zip(rgb[at:at+3],color))>12:raise AssertionError('Corrupt or missing detached pixels')
        fields=[]
        for k in range(6):
            value=0
            for bit in range(16):
                at=(404*640+32+(k*16+bit)*6+2)*3
                if sum(rgb[at:at+3])>384:value|=1<<bit
            fields.append(value)
        if fields[0]!=0x444c or min(fields[2:4])<4:raise AssertionError('Insufficient resolution swaps')
        publications.append(fields[2:4])
    return dict(validatedTailFrames=30,rgba8Publications=publications[-1][0],cmprPublications=publications[-1][1],pixelPatches=8)
if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('dol',type=Path);p.add_argument('--logs',type=Path,required=True);p.add_argument('--seconds',type=float,default=25);p.add_argument('--backend',choices=['OGL','Software'],default='OGL')
    a=p.parse_args();a.streaming=a.benchmark=False;a.mode='detached';a.binary='/usr/games/dolphin-emu-nogui'
    run(a,validate_capture)
