#!/usr/bin/env python3
"""Original synthetic firmware only: console bridge, mounted-file fallback and GC."""
import os
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
        image=bytearray(0x200000);header=b'Nintendo GameCube IPL\r\nSynthetic fixture'
        image[:len(header)]=header
        for offset in (0x1000,0x2000):
            image[offset:offset+24]=b'Yay0'+(4).to_bytes(4,'big')+(20).to_bytes(4,'big')*2+bytes([240,0,0,0])+b'ABCD'
        path=sd/'firmware-synthetic.bin';path.write_bytes(image)
        generated=work/'firmware.c'
        run('dotnet',ROOT/'tools/DolphinDotNet.Compiler/bin/Release/net8.0/DolphinDotNet.Compiler.dll','--aot',ROOT/'tests/SystemFirmwareReadSmoke/bin/Release/net8.0/SystemFirmwareReadSmoke.dll',generated)
        run(os.environ.get('CC','cc'),'-pthread','-O1','-std=c11','-Wall','-Wextra','-Werror','-Wno-unused-function','-Iinclude',generated,'tests/system_firmware_read_host.c',
            'source/managed_runtime.c','source/async_runtime.c','source/text.c','source/numeric.c','source/storage.c','source/contiguous_write.c','source/file_optimize.c','-lm','-o',work/'read-host')
        run(work/'read-host',work,path)
if __name__=='__main__':main()
