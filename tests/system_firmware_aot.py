#!/usr/bin/env python3
"""Original synthetic firmware only: console bridge, mounted-file fallback and GC."""
import os
import struct
from pathlib import Path
import subprocess
import tempfile
from generate_system_model_fixture import build_model
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
        # Original synthetic TEX1 and adjacent compact WSYS/AW fixtures.
        model=bytearray(0x6140);model[:8]=b'J3D1bmd1';big(model,8,len(model)-128);big(model,12,2)
        model[32:36]=b'INF1';big(model,36,8416-32)
        model[8416:8420]=b'TEX1';big(model,8420,len(model)-8416);model[8425]=1;big(model,8428,32)
        model[8451]=8;model[8453]=8;model[8472]=1;big(model,8476,32)
        masks=bytes([255])*(((len(model)+31)//32)*4);encoded=b'Yay0'+struct.pack('>III',len(model),16+len(masks),16+len(masks))+masks+model
        image[0x12000:0x12000+len(encoded)]=encoded
        geometry=build_model();masks=bytes([255])*(((len(geometry)+31)//32)*4)
        encoded=b'Yay0'+struct.pack('>III',len(geometry),16+len(masks),16+len(masks))+masks+geometry
        image[0x19000:0x19000+len(encoded)]=encoded
        descriptor=bytearray(1024);descriptor[:4]=b'WSYS';big(descriptor,4,len(descriptor));big(descriptor,16,32)
        descriptor[32:36]=b'WINF';big(descriptor,36,1);big(descriptor,40,64);descriptor[64:72]=b'ipl_0.aw'
        # Compact WSYS bank family; desktop fixtures also cover extended names.
        big(descriptor,132,12);offset=0;archive=bytearray(196160)
        for channel in range(12):
            wave=256+channel*36;samples=174067 if channel<2 else 16;size=(samples+15)//16*9
            big(descriptor,136+channel*4,wave);big(descriptor,wave+4,0x46fa0000);big(descriptor,wave+8,offset)
            big(descriptor,wave+12,size);big(descriptor,wave+24,samples);big(descriptor,wave+28,samples)
            if channel<2:archive[offset:offset+size]=bytes([0]+[0x11 if channel==0 else 0xee]*8)*10880
            offset=(offset+size+31)&~31
        # Original generated ID tables exercise bounded C-DF parsing under AOT.
        big(descriptor,20,700);descriptor[700:704]=b'WBCT';big(descriptor,708,1);big(descriptor,712,720)
        descriptor[720:724]=b'SCNE';big(descriptor,732,752);descriptor[752:756]=b'C-DF';big(descriptor,756,12)
        for index in range(12):big(descriptor,760+index*4,808+index*4);big(descriptor,808+index*4,100+index)
        image[0x20000:0x20400]=descriptor;big(image,0x1ffe8,2);big(image,0x1ffec,1152);big(image,0x1fff0,1152)
        image[0x20400:0x20400+len(archive)]=archive
        for at,text in ((0x1aff00,b'FONT'),(0x1fcf00,b'GLYP')):
            image[at:at+24]=b'Yay0'+struct.pack('>III',4,20,20)+bytes([240,0,0,0])+text
        path=sd/'firmware-synthetic.bin';path.write_bytes(image)
        bundle=work/'bundle'
        compiler=ROOT/'tools/DolphinDotNet.Compiler/bin/Release/net8.0/DolphinDotNet.Compiler.dll'
        assembly=ROOT/'tests/SystemFirmwareReadSmoke/bin/Release/net8.0/SystemFirmwareReadSmoke.dll'
        run('dotnet',compiler,'--bundle',assembly,bundle)
        assert 'DND_MANAGED_HEAP_BYTES ?= 8388608' in (bundle/'Makefile').read_text()
        generated=work/'firmware.c'
        run('dotnet',ROOT/'tools/DolphinDotNet.Compiler/bin/Release/net8.0/DolphinDotNet.Compiler.dll','--aot',ROOT/'tests/SystemFirmwareReadSmoke/bin/Release/net8.0/SystemFirmwareReadSmoke.dll',generated)
        run(os.environ.get('CC','cc'),'-pthread','-O1','-std=c11','-Wall','-Wextra','-Werror','-Wno-unused-function','-DDND_FIRMWARE_TEST','-Iinclude',generated,'tests/system_firmware_read_host.c','source/system_firmware.c',
            *sorted(str(p.relative_to(ROOT)) for p in (ROOT/'source').glob('model*.c')),'source/aram.c','source/audio.c','source/draw_resources.c','source/graphics_device.c','source/graphics_gx.c','source/performance.c','source/managed_runtime.c','source/async_runtime.c','source/text.c','source/numeric.c','source/storage.c','source/contiguous_write.c','source/file_optimize.c','source/verified_copy.c','-lm','-o',work/'read-host')
        run(work/'read-host',work,path)
if __name__=='__main__':main()
