#!/usr/bin/env python3
"""Metadata inventory CLI, original synthetic records only."""
import json
from pathlib import Path
import struct
import subprocess
import tempfile
ROOT=Path(__file__).resolve().parents[1]
def main():
    subprocess.run(['dotnet','build','tools/DolphinDotNet.Assets','-c','Release','-p:NuGetAudit=false'],cwd=ROOT,check=True)
    tool=ROOT/'tools/DolphinDotNet.Assets/bin/Release/net8.0/DolphinDotNet.Assets.dll'
    with tempfile.TemporaryDirectory() as tmp:
        path=Path(tmp)/'synthetic.bin';data=bytearray(0x200000);header=b'Nintendo GameCube IPL synthetic CLI'
        data[:len(header)]=header
        record=b'Yay0'+struct.pack('>III',4,20,20)+bytes([240,0,0,0])+b'ABCD'
        for at in (0x1000,0x2000):data[at:at+len(record)]=record
        path.write_bytes(data)
        result=subprocess.run(['dotnet',str(tool),'firmware','inspect',str(path)],cwd=ROOT,check=True,capture_output=True,text=True)
        report=json.loads(result.stdout)
        assert report['resourceCount']==2 and report['source']=='ExplicitFile'
        assert report['encoding']=='Descrambled' and report['geometrySupported'] is False
        assert [entry['offset'] for entry in report['resources']]==[0x1000,0x2000]
        assert report['files']==[] and 'unsupported' in report['startupAudio']
        assert all('unsupported' in profile for profile in report['textures'].values())
        assert not any(key in report for key in ('data','rom','samples','pixels','vertices'))
        path.write_bytes(b'truncated')
        bad=subprocess.run(['dotnet',str(tool),'firmware','inspect',str(path)],cwd=ROOT,capture_output=True,text=True)
        assert bad.returncode!=0 and 'exactly 2 MiB' in bad.stderr
    print('Firmware inventory CLI metadata and malformed-source checks passed')
if __name__=='__main__':main()
