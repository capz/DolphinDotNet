#!/usr/bin/env python3
"""Original small GameCube audio fixtures; no game assets or encoder dependencies."""
import struct, pathlib, sys
out=pathlib.Path(sys.argv[1]);out.mkdir(parents=True,exist_ok=True)
def u(p,a,n):struct.pack_into('>I',p,a,n&0xffffffff)
def h(p,a,n):struct.pack_into('>H',p,a,n&65535)
def le(p,a,n):struct.pack_into('<I',p,a,n)
def save(n,p):(out/n).write_bytes(p)
def frames(count=2,size=8):
 p=bytearray(count*size)
 for i in range(count):p[i*size+1]=0x12 if i==0 else 0x34;p[i*size+2]=0xf8
 return p
def dsp():
 p=bytearray(112);u(p,0,28);u(p,4,32);u(p,8,22050);h(p,12,1);u(p,16,5);u(p,20,23);u(p,24,2);p[96:]=frames();return p
def adx(codec=2,version=0x300):
 p=bytearray(64+72);h(p,0,0x8000);h(p,2,60);p[4:8]=bytes([codec,18,4,2]);u(p,8,22050);u(p,12,64);h(p,16,500);h(p,18,version);p[58:64]=b'(c)CRI'
 loop=20 if version==0x300 else 32;u(p,loop+4,1);u(p,loop+8,3);u(p,loop+16,40)
 for f in range(4):p[64+f*18+2]=0x12;p[64+f*18+3]=0xf8
 return p
def afc():
 p=bytearray(68);u(p,0,len(p));u(p,4,32);h(p,8,22050);h(p,10,4);h(p,12,16);u(p,16,1);u(p,20,3)
 for f in range(4):p[32+f*9+1]=0x12;p[32+f*9+2]=0xf8
 return p
def loop_adx(channels,version):
 # Different frames/channels and nonzero predictors expose stale loop histories.
 offset=96;count=117;p=bytearray(offset+4*18*channels)
 h(p,0,0x8000);h(p,2,offset-4);p[4:8]=bytes([3,18,4,channels]);u(p,8,22050);u(p,12,count);h(p,16,500);h(p,18,version);p[offset-6:offset]=b'(c)CRI'
 loop=20
 if version==0x400:
  h(p,24,123);h(p,26,-47)
  if channels==2:h(p,28,-81);h(p,30,21)
  loop=32
 if version!=0x500:
  u(p,loop,1);u(p,loop+4,1);u(p,loop+8,35);u(p,loop+12,offset+18*channels);u(p,loop+16,101);u(p,loop+20,offset+4*18*channels)
 for f in range(4):
  for c in range(channels):
   at=offset+(f*channels+c)*18;h(p,at,3+f+c)
   for n in range(16):p[at+2+n]=(((n+f*3+c*5)%15+1)<<4)|((n*3+f+c*7)%16)
 return p
def hps():
 p=bytearray(224);p[:8]=b' HALPST\0';u(p,8,22050);u(p,12,2);u(p,24,31);u(p,80,31)
 for a,next_ in [(128,176),(176,176)]:u(p,a,16);u(p,a+4,15);u(p,a+8,next_);p[a+32:a+40]=frames(1);p[a+40:a+48]=frames(1)
 return p
def ssm():
 p=bytearray(184);u(p,0,136);u(p,4,32);u(p,8,1);u(p,16,2);u(p,20,22050)
 for c in range(2):a=24+c*64;h(p,a,1);u(p,a+4,7+c*32);u(p,a+8,34+c*32);u(p,a+12,2+c*32)
 p[152:168]=frames();p[168:184]=frames();return p
def ast(codec=0):
 size=9 if codec==0 else 32;p=bytearray(64+2*(32+size*2));p[:4]=b'STRM';u(p,4,len(p)-64);h(p,8,codec);h(p,10,16);h(p,12,2);h(p,14,1);u(p,16,22050);u(p,20,32);u(p,24,3);u(p,28,28)
 for i in range(2):a=64+i*(32+size*2);p[a:a+4]=b'BLCK';u(p,a+4,size)
 for i in range(2):
  a=64+i*(32+size*2)+32
  for c in range(2):
   if codec==0:p[a+c*size+1:a+c*size+3]=b'\x12\xf8'
   else:h(p,a+c*size,1);h(p,a+c*size+2,2);h(p,a+c*size+4,-1);h(p,a+c*size+6,-8)
 return p
def wsys(codec=0):
 p=bytearray(256);p[:4]=b'WSYS';u(p,4,256);u(p,16,32);p[32:36]=b'WINF';u(p,36,1);u(p,40,64);p[64:73]=b'voice.aw\0';u(p,176,1);u(p,180,192);p[193]=codec;struct.pack_into('>f',p,196,22050);size={0:18,1:10,2:32,3:64}[codec];u(p,204,size);u(p,208,0xffffffff);u(p,212,3);u(p,216,24);u(p,220,32)
 body=frames(2,9 if codec==0 else 5) if codec in [0,1] else bytearray(size)
 if codec==2:body[0:4]=bytes([1,2,255,248])
 if codec==3:h(body,0,1);h(body,2,2);h(body,4,-1);h(body,6,-8)
 return p,body
def aaf(w):
 p=bytearray(64)+w;u(p,0,1);u(p,4,0);u(p,8,0);u(p,16,2);u(p,24,3);u(p,28,64);u(p,32,len(w));return p
def re4():
 p=bytearray(192);u(p,0,1);u(p,4,32);u(p,8,16);h(p,16,0);u(p,32,16);a=48;u(p,a,0);u(p,a+4,28);u(p,a+8,32);u(p,a+12,22050);u(p,a+16,2);u(p,a+20,5);u(p,a+24,23);return p,frames()
def rw(id_,data):return struct.pack('<III',id_,len(data),0x1803ffff)+data
def rws809():
 p=bytearray(320);u(p,4,22050);u(p,12,16);p[17]=1;u(p,24,96);u(p,32,0xf86215b0)
 stream=rw(0x802,rw(0x803,p)+rw(0x804,frames()))
 return rw(0x809,rw(0x80a,bytes(64))+rw(0x80c,struct.pack('>I',1)+stream))
def rws80d():
 p=bytearray(512+12+96);le(p,0,0x80d);le(p,4,len(p)-12);le(p,12,0x80e);le(p,16,488);u(p,56,1);u(p,64,1);p[104:109]=b'test\0';u(p,144,96);u(p,148,0);u(p,152,64);p[172:177]=b'song\0';u(p,204,48);h(p,212,16);h(p,214,8);u(p,220,32);u(p,224,0);u(p,228,22050);p[241]=2;u(p,256,0xf86215b0);le(p,512,0x80f);le(p,516,96)
 for a in [524,572]:p[a:a+16]=frames();p[a+16:a+32]=frames()
 return p
def afs():
 a=adx();p=bytearray(256+len(a));p[:4]=b'AFS\0';le(p,4,2);le(p,8,64);le(p,12,len(a));le(p,16,256);le(p,20,len(a));p[64:64+len(a)]=a;p[256:]=a;return p
save('tone.dsp',dsp());p=dsp();p[96]=0x10;h(p,32,1024);h(p,64,10);save('history.dsp',p)
for codec in [2,3,4]:
 for version in [0x300,0x400,0x500]:save(f'type-{codec}-{version}.adx',adx(codec,version))
save('tone.adx',adx());save('tone.afs',afs());save('tone.afc',afc());save('tone.hps',hps());save('tone.ssm',ssm());save('tone.ast',ast());save('pcm.ast',ast(1));save('tone.rws',rws80d());save('old.rws',rws809())
for channels in [1,2]:
 for version in [0x300,0x400,0x500]:save(f'loop-{channels}-{version}.adx',loop_adx(channels,version))
p=loop_adx(2,0x400);a=bytearray(64)+p;a[:4]=b'AFS\0';le(a,4,1);le(a,8,64);le(a,12,len(p));save('loop.afs',a)
for name,start,end in [('empty-loop.adx',35,35),('reversed-loop.adx',40,35),('bad-loop.adx',35,118),('negative-loop.adx',-1,101)]:
 p=loop_adx(2,0x400);u(p,40,start);u(p,48,end);save(name,p)
p=loop_adx(1,0x300);u(p,24,0);u(p,28,-1);u(p,36,0);save('no-loop.adx',p)
for codec in range(4):
 w,b=wsys(codec);save(f'wave-{codec}.wsys',w);save(f'wave-{codec}.aaf',aaf(w));save(f'wave-{codec}.aw',b)
hd,body=re4();save('tone.sbh',hd);save('tone.sbb',body)
p=bytearray(64)
for a in [0,32]:p[a:a+4]=b'\x0c\x0c\x0c\x0c';p[a+4]=0x21;p[a+5]=0x8f
save('tone.dtk',p)
for name,data in [('bad-predictor.dsp',dsp()),('bad-loop.dsp',dsp()),('bad-block.hps',hps()),('bad-codec.rws',rws80d()),('encrypted.adx',adx()),('cycle.aaf',aaf(wsys()[0]))]:
 if name=='bad-predictor.dsp':data[96]=0xf0
 elif name=='bad-loop.dsp':u(data,16,2000)
 elif name=='bad-block.hps':u(data,184,120)
 elif name=='bad-codec.rws':u(data,256,0xd9ea9798)
 elif name=='encrypted.adx':h(data,18,0x409)
 else:u(data,64+40,0xffffffff)
 save(name,data)
print('Generated original GameCube audio fixtures:',out)
# Long enough to guarantee multiple background read slices for cancellation tests.
p=bytearray(96+512*1024);u(p,0,512*1024//8*14);u(p,4,512*1024*2);u(p,8,22050);u(p,24,2);save('large.dsp',p)

p=bytearray(1040);u(p,0,0x809);u(p,8,1024);u(p,12,64);u(p,24,0xfd9d32d3);u(p,40,1024);u(p,76,128);u(p,80,128);u(p,128,76);u(p,132,76);u(p,136,160);u(p,176,22050);u(p,180,3);u(p,184,16);p[189]=1;u(p,192,256);p[1024:]=frames();save('tone.awd',p)
# More than 16 MiB after PCM expansion, while encoded data fits the default budget.
p=bytearray(96+4800000);u(p,0,4800000//8*14);u(p,4,4800000*2);u(p,8,22050);u(p,24,2);save('music.dsp',p)

import wave
with wave.open(str(out/'tone.wav'),'wb') as w:
 w.setnchannels(1);w.setsampwidth(2);w.setframerate(22050);w.writeframes(bytes(64))

p=hps();h(p,36,1024);h(p,142,10);h(p,190,20);p[160]=0x10;p[208]=0x10;save('history.hps',p)
p=adx(3);h(p,16,0);save('zero-cutoff.adx',p)
