#!/usr/bin/env python3
"""Create an original, tiny GameCube ISO for DiscBrowser acceptance (no game assets)."""
from pathlib import Path
import struct
import sys


def create(path, many_files=False):
    image=bytearray(0x10000)
    def put(at,value):struct.pack_into('>I',image,at,value)
    def text(at,value):
        data=value.encode('ascii')
        image[at:at+len(data)]=data
    text(0,'TEST01')
    text(0x20,'Fixture disc')
    put(0x1c,0xc2339f3d)
    put(0x424,0x500)
    entries=[('',True,0,0),('dir',True,0,3),('data',False,0x1800,5),
             ('top',False,0x2800,3),('opening.bnr',False,0x4000,0x1960)]
    if many_files:
        entries.extend((f'file{i}.bin',False,0x2800,3) for i in range(12))
    entries[0]=('',True,0,len(entries))
    names=bytearray()
    for i,(name,directory,start,length) in enumerate(entries):
        put(0x500+12*i,(0x01000000 if directory else 0)|len(names))
        put(0x504+12*i,start)
        put(0x508+12*i,length)
        names.extend(name.encode('ascii')+b'\0')
    string_offset=0x500+12*len(entries)
    image[string_offset:string_offset+len(names)]=names
    put(0x428,12*len(entries)+len(names))
    text(0x1800,'hello')
    text(0x2800,'abc')
    text(0x4000,'BNR1')
    image[0x4020:0x5820]=b'\xfc\x00'*(96*32)
    text(0x5820,'Fixture short title')
    text(0x5860,'Fixture banner')
    text(0x58e0,'Synthetic ISO access test')
    Path(path).write_bytes(image)


if __name__=='__main__':create(sys.argv[1], '--many-files' in sys.argv[2:])
