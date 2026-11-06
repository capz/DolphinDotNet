#!/usr/bin/env python3
"""Check external firmware capture metadata; never import image/ROM payloads.

Usage: python3 tests/check_system_firmware_captures.py /tmp/captures/results.json
The hardware automation runner writes this JSON next to external PNGs.
"""
import json
from pathlib import Path
import sys

def check(records):
    images={Path(record['png']).name:record['sha256'] for record in records}
    textures=['environment','tiles','logo-top','logo-left','logo-right','wordmark']
    models=['base-cube','cover-cube','logo','wordmark-model','small-cube','menu-cube','menu-icon-0','menu-icon-1','memory-cube']
    animation=['drop','trace-early','trace-late','assemble','letters','hold','fade']
    for name in textures+models+animation:
        if name+'.png' not in images:raise ValueError('Missing capture: '+name)
        stable=name+'-stable.png'
        if stable in images and images[stable]!=images[name+'.png']:
            raise ValueError('Fixed snapshot changed between frames: '+name)
    if len({images[n+'.png'] for n in textures})!=6:
        raise ValueError('Texture captures are not distinct; check real XFB RAM copies.')
    if len({images[n+'.png'] for n in animation})<5:
        raise ValueError('Animation captures do not show enough distinct stages.')
    if images['hold.png']==images['fade.png']:
        raise ValueError('Fade did not change the held image.')
    for record in records:
        if record['width'] not in (640,):raise ValueError('Unexpected fixture width')
        if record['height'] not in (480,528,576):raise ValueError('Unexpected fixture height')
        if record['rawBytes']!=record['width']*record['height']*2:raise ValueError('Incorrect XFB byte count')
    return len(records)

if __name__=='__main__':
    print('Firmware external captures passed:',check(json.loads(Path(sys.argv[1]).read_text())))
