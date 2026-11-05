#!/usr/bin/env python3
"""Original synthetic firmware only: console bridge, mounted-file fallback and GC."""
import os
import struct
from pathlib import Path
import subprocess
import tempfile
ROOT=Path(__file__).resolve().parents[1]
def run(*args):
    subprocess.run(list(map(str,args)),cwd=ROOT,check=True)
def main():
    run('dotnet','build','tools/DolphinDotNet.Compiler','-c','Release','-p:NuGetAudit=false')
    run('dotnet','build','tests/SystemFirmwareReadSmoke','-c','Release','-p:NuGetAudit=false')
    with tempfile.TemporaryDirectory() as tmp:
        work=Path(tmp);sd=work/'sd';sd.mkdir()
        image=bytearray(0x200000);header=b'(C) 2099 Nintendo. Generated fixture.(C) 2099 ArtX Inc. Generated fixture.NTSC Revision 9.9'
        image[:len(header)]=header
        for offset in (0x1000,0x2000):
            image[offset:offset+24]=b'Yay0'+(4).to_bytes(4,'big')+(20).to_bytes(4,'big')*2+bytes([240,0,0,0])+b'ABCD'
        def big(data,at,value):struct.pack_into('>I',data,at,value)
        image[0x7000:0x7010]=b'Yay0'+struct.pack('>III',4,0xffffffff,16) # malformed marker must be ignored through typed AOT catches
        # Original structural TEX1 and standalone WSYS/AW/FST fixtures.
        model=bytearray(0x6140);model[:8]=b'J3D1bmd1';big(model,8,len(model)-128);big(model,12,2)
        model[32:36]=b'INF1';big(model,36,8416-32)
        model[8416:8420]=b'TEX1';big(model,8420,len(model)-8416);model[8425]=1;big(model,8428,32)
        model[8451]=8;model[8453]=8;model[8472]=1;big(model,8476,32)
        masks=bytes([255])*(((len(model)+31)//32)*4);encoded=b'Yay0'+struct.pack('>III',len(model),16+len(masks),16+len(masks))+masks+model
        image[0x12000:0x12000+len(encoded)]=encoded
        descriptor=bytearray(320);descriptor[:4]=b'WSYS';big(descriptor,4,320);big(descriptor,16,32)
        descriptor[32:36]=b'WINF';big(descriptor,36,1);big(descriptor,40,64);descriptor[64:72]=b'ipl_0.aw'
        # Compact WSYS bank family; desktop fixtures also cover extended names.
        big(descriptor,132,2);big(descriptor,136,192);big(descriptor,140,232)
        for channel in range(2):
            wave=192+channel*40;big(descriptor,wave+4,0x46fa0000);big(descriptor,wave+8,channel*97920)
            big(descriptor,wave+12,97920);big(descriptor,wave+24,174080);big(descriptor,wave+28,174080)
        image[0x20000:0x20140]=descriptor
        image[0x22000:0x22000+195840]=(bytes([0]+[0x11]*8)*10880+bytes([0]+[0xee]*8)*10880)
        table=0x4000
        for index,values in enumerate(((0x01000000,0,5),(0x01000001,0,3),(7,0x20000,320),(14,0x22000,195840),(23,0x1000,24))):
            struct.pack_into('>III',image,table+12*index,*values)
        names=b'\0Banks\0ipl.ws\0ipl_0.aw\0cube.bin\0';image[table+60:table+60+len(names)]=names
        path=sd/'firmware-synthetic.bin';path.write_bytes(image)
        bundle=work/'bundle'
        compiler=ROOT/'tools/DolphinDotNet.Compiler/bin/Release/net8.0/DolphinDotNet.Compiler.dll'
        assembly=ROOT/'tests/SystemFirmwareReadSmoke/bin/Release/net8.0/SystemFirmwareReadSmoke.dll'
        run('dotnet',compiler,'--bundle',assembly,bundle)
        assert 'DND_MANAGED_HEAP_BYTES ?= 8388608' in (bundle/'Makefile').read_text()
        generated=work/'firmware.c'
        run('dotnet',ROOT/'tools/DolphinDotNet.Compiler/bin/Release/net8.0/DolphinDotNet.Compiler.dll','--aot',ROOT/'tests/SystemFirmwareReadSmoke/bin/Release/net8.0/SystemFirmwareReadSmoke.dll',generated)
        run(os.environ.get('CC','cc'),'-pthread','-O1','-std=c11','-Wall','-Wextra','-Werror','-Wno-unused-function','-Iinclude',generated,'tests/system_firmware_read_host.c',
            'source/audio.c','source/draw_resources.c','source/graphics_device.c','source/graphics_gx.c','source/performance.c','source/managed_runtime.c','source/async_runtime.c','source/text.c','source/numeric.c','source/storage.c','source/contiguous_write.c','source/file_optimize.c','-lm','-o',work/'read-host')
        run(work/'read-host',work,path)
if __name__=='__main__':main()
