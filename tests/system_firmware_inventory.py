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
        assert report['encoding']=='Descrambled' and report['geometryValidation']=='NativeImportRequired'
        assert [entry['offset'] for entry in report['resources']]==[0x1000,0x2000]
        assert report['files']==[] and 'unsupported' in report['startupAudio']
        assert all('unsupported' in profile for profile in report['textures'].values())
        assert not any(key in report for key in ('data','rom','samples','pixels','vertices'))
        assert 'menuAudio' not in report
        menu=subprocess.run(['dotnet',str(tool),'firmware','inspect',str(path),'--menu-audio'],cwd=ROOT,check=True,capture_output=True,text=True)
        section=json.loads(menu.stdout)['menuAudio']
        assert section['retainedEncodedBytes']==0 and section['candidates']==[]
        assert len(section['inputSha256'])==64 and len(section['effects'])==11
        assert all('unsupported' in effect for effect in section['effects'].values())
        # Generated original adjacent banks cover both source layouts and
        # nonzero padding. Inspect must remain metadata-only; export is explicit.
        for compact in (False,True):
            audio=bytearray(data);at=0x5000
            def big(offset,value):struct.pack_into('>I',audio,offset,value)
            audio[at:at+4]=b'WSYS';big(at+4,1024);big(at+16,32)
            audio[at+32:at+36]=b'WINF';big(at+36,1);big(at+40,64)
            audio[at+64:at+72]=b'ipl_0.aw';names=68 if compact else 112
            big(at+64+names,12);big(at-24,2);big(at-20,1152);big(at-16,1152)
            offset=0
            for index in range(12):
                wave=256+index*(36 if compact else 44);samples=174067 if index<2 else 19
                size=(samples+15)//16*9
                big(at+64+names+4+index*4,wave);big(at+wave+4,0x46fa0000)
                big(at+wave+8,offset);big(at+wave+12,size);big(at+wave+28,samples)
                stop=(offset+size+31)&~31
                audio[0x5400+offset+size:0x5400+stop]=bytes([0x77])*(stop-offset-size)
                offset=stop
            path.write_bytes(audio)
            result=subprocess.run(['dotnet',str(tool),'firmware','inspect',str(path),'--menu-audio'],cwd=ROOT,check=True,capture_output=True,text=True)
            section=json.loads(result.stdout)['menuAudio'];candidate=section['candidates'][2]
            assert len(section['candidates'])==12 and candidate['bankIndex']==0 and candidate['bankLocalIndex']==2
            assert candidate['flattenedIndex']==2 and candidate['sampleFrames']==19
            assert candidate['association']['layout']=='AdjacentWaveArchive'
            assert candidate['descriptorBytes']==(36 if compact else 44)
            assert section['retainedEncodedBytes']==0 and all('unsupported' in effect for effect in section['effects'].values())
            output=Path(tmp)/('compact' if compact else 'extended')
            command=['dotnet',str(tool),'firmware','audition',str(path),'0','2',str(output)]
            subprocess.run(command,cwd=ROOT,check=True,capture_output=True)
            assert (output/'selected.wsys').stat().st_size==256 and (output/'selected.aw').stat().st_size==18
            assert struct.unpack_from('>I',(output/'selected.wsys').read_bytes(),200)[0]==0
            assert subprocess.run(command,cwd=ROOT,capture_output=True).returncode!=0
            assert (output/'selected.wsys').stat().st_size==256 and (output/'selected.aw').stat().st_size==18
        path.write_bytes(b'truncated')
        bad=subprocess.run(['dotnet',str(tool),'firmware','inspect',str(path)],cwd=ROOT,capture_output=True,text=True)
        assert bad.returncode!=0 and 'exactly 2 MiB' in bad.stderr
    print('Firmware inventory CLI metadata and malformed-source checks passed')
if __name__=='__main__':main()
