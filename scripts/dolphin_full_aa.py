"""Sustained native full-AA layout, seam and in-place capture oracle (host MSAA off)."""
import argparse
from pathlib import Path
from dolphin_texture_test import FRAME_BYTES,run

def validate(raw):
    frames=len(raw)//FRAME_BYTES
    if len(raw)%FRAME_BYTES or frames<20:raise AssertionError('Insufficient complete frames')
    for offset in range((frames-10)*FRAME_BYTES,len(raw),FRAME_BYTES):
        rgb=raw[offset:offset+FRAME_BYTES]
        def pixel(x,y):return tuple(rgb[(y*640+x)*3:(y*640+x)*3+3])
        # Fine stripes must continue on both sides of the join, at the same phase.
        for y in range(16,464,4):
            if max(pixel(80,y))<120 or min(pixel(80,y+2))>110:
                raise AssertionError(f'Lost full-height stripe detail at row {y}')
        # The 4-row crossbar crosses the join. No missing/duplicated half-screen.
        for y in (239,240):
            color=pixel(300,y)
            if color[1]<120 or color[1]<color[0]*1.5:raise AssertionError(f'AA seam at {y}: {color}')
        for y in (40,200,280,440):
            x=round(190+(y-10)*265/460)
            color=pixel(x,y)
            if color[0]<120 or color[0]<color[1]*1.5:raise AssertionError(f'Diagonal/projection lost at {x},{y}: {color}')
    return {'frames':frames,'logicalSize':[640,480],'passes':2,'hostMsaa':1,'seam':True,'fullHeightDetail':True}

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('dol',type=Path);p.add_argument('--logs',type=Path,required=True)
    p.add_argument('--seconds',type=float,default=20)
    p.add_argument('--binary',default='/usr/games/dolphin-emu-nogui')
    args=p.parse_args();args.backend='OGL';args.mode='pages';args.streaming=False;args.benchmark=False
    run(args,validate)
