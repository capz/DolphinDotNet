#!/usr/bin/env python3
"""Capture the actual offline Kart frame using Dolphin's software renderer."""
import argparse
from pathlib import Path
import dolphin_shader_test as capture

def validate(rgb):
    if len(rgb)!=640*480*3:raise AssertionError('Incomplete kart frame')
    def pixel(x,y):
        offset=(y*640+x)*3
        return tuple(rgb[offset:offset+3])
    sky=pixel(320,100);kart=pixel(310,325);road=pixel(300,400)
    if not sky[2]>sky[0]+60:raise AssertionError(f'Missing island sky: {sky}')
    if not kart[0]>kart[2]+40:raise AssertionError(f'Missing orange player kart: {kart}')
    if not 40<road[0]<130 or max(road)-min(road)>55:raise AssertionError(f'Missing track: {road}')
    return {'sky':sky,'kart':kart,'road':road}

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('dol',type=Path)
    parser.add_argument('--binary',default='/usr/games/dolphin-emu-nogui')
    parser.add_argument('--logs',type=Path,default=Path('dolphin-kart'))
    parser.add_argument('--seconds',type=float,default=90)
    capture.validate=validate
    capture.run(parser.parse_args())
