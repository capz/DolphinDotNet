#!/usr/bin/env python3
"""Generated J3D1/MAT1 triangle; contains no original firmware/model bytes."""
import struct
from pathlib import Path

def build_model():
    def word(d,at,value):struct.pack_into('>I',d,at,value)
    def half(d,at,value):struct.pack_into('>H',d,at,value)
    def floats(d,at,*values):struct.pack_into('>'+len(values)*'f',d,at,*values)
    def chunk(tag,size):
        d=bytearray(size);d[:4]=tag;word(d,4,size);return d
    def names(value):
        return struct.pack('>HHHH',1,0xffff,0,8)+value.encode()+b'\0'
    inf=chunk(b'INF1',64);half(inf,10,0xffff);word(inf,12,1);word(inf,16,3);word(inf,20,24)
    for i,pair in enumerate(((0x10,0),(1,0),(0x11,0),(1,0),(0x12,0),(2,0),(2,0),(0,0))):
        struct.pack_into('>HH',inf,24+i*4,*pair)
    vtx=chunk(b'VTX1',288);word(vtx,8,64);word(vtx,12,128);word(vtx,16,192);word(vtx,32,256)
    for i,(attr,components) in enumerate(((9,1),(10,0),(13,1),(255,0))):
        struct.pack_into('>IIII',vtx,64+i*16,attr,components,4,0)
    floats(vtx,128,-1,-1,0,1,-1,0,0,1,0);floats(vtx,192,0,0,1,0,0,1,0,0,1)
    floats(vtx,256,0,0,1,0,.5,1)
    evp=chunk(b'EVP1',32);half(evp,10,0xffff)
    drw=chunk(b'DRW1',32);half(drw,8,1);half(drw,10,0xffff);word(drw,12,20);word(drw,16,22)
    jnt=chunk(b'JNT1',128);half(jnt,8,1);half(jnt,10,0xffff)
    word(jnt,12,24);word(jnt,16,88);word(jnt,20,92);half(jnt,24,0);half(jnt,26,0xffff)
    floats(jnt,28,1,1,1);floats(jnt,60,1)
    text=names('boot_demo_base_cube');jnt[92:92+len(text)]=text
    shp=chunk(b'SHP1',192);half(shp,8,1);half(shp,10,0xffff)
    for offset,value in ((12,44),(16,84),(24,88),(28,120),(32,128),(36,160),(40,168)):word(shp,offset,value)
    shp[45]=255;half(shp,46,1);half(shp,54,0xffff);floats(shp,56,1)
    for i,pair in enumerate(((9,3),(10,3),(13,3),(255,0))):struct.pack_into('>II',shp,88+i*8,*pair)
    shp[128]=0x90;half(shp,129,3)
    for i in range(3):struct.pack_into('>HHH',shp,131+i*6,i,i,i)
    half(shp,162,1);word(shp,164,0);word(shp,168,32)
    mat=bytearray(280);mat[:4]=b'MAT1';half(mat,8,1);word(mat,12,100);word(mat,16,252);word(mat,20,256)
    mat[100:252]=bytes([255])*152;mat[100:108]=bytes([4,0,0,0,0,0,0,0])
    for offset,value in ((8,0),(12,0),(14,1),(20,0),(36,0),(56,0),(72,0),(104,0),(106,1),(108,2),(112,0),(146,0),(148,0)):
        half(mat,100+offset,value)
    text=names('Generated');mat[256:256+len(text)]=text
    def append(header,value):
        mat.extend(bytes((-len(mat))%4));word(mat,header,len(mat));mat.extend(value)
    append(24,struct.pack('>I',1));append(28,bytes([255,255,255,128]));append(32,b'\1')
    append(36,bytes([0,0,0,2,2,255,255,255])*2);append(40,b'\1');append(44,bytes([1,1,30,0]))
    append(48,struct.pack('>12f',.5,0,0,.5,0,.5,0,.5,0,0,1,0)+bytes([1,0,0,0]))
    append(52,b'\0\0');append(56,bytes([0,0,4,0]))
    append(60,struct.pack('>12h',-12,20,40,255,255,255,255,255,255,255,255,255))
    append(64,b'\1');append(68,bytes([255,15,2,8,15,0,0,1,0,0,7,5,4,7,0,0,0,1,0,0]))
    append(72,b'');append(76,bytes([7,0,1,7,0,255,255,255]));append(80,bytes([1,4,5,3]))
    append(84,bytes([1,3,0,255]));append(88,b'\1');append(92,b'\1');append(96,bytes(12))
    mat.extend(bytes((-len(mat))%32));word(mat,4,len(mat))
    tex=chunk(b'TEX1',160);half(tex,8,1);word(tex,12,32);word(tex,16,64)
    tex[32]=1;half(tex,34,8);half(tex,36,8);tex[52]=tex[53]=1;tex[56]=1;word(tex,60,64)
    text=names('GeneratedTexture');tex[64:64+len(text)]=text;tex[96:160]=bytes([192])*64
    data=bytearray(32);data[:8]=b'J3D1bmd1';word(data,12,8)
    for d in (inf,vtx,evp,drw,jnt,shp,mat,tex):data.extend(d)
    word(data,8,len(data)-32) # Real early files can retain a stale size field.
    return data

if __name__=='__main__':
    import sys
    Path(sys.argv[1]).write_bytes(build_model())
