#!/usr/bin/env python3
"""Generated selected-wave vectors through the real managed/native bridge.
No retail firmware, reference samples or proprietary audio are used.
"""
import os
from pathlib import Path
import subprocess
import tempfile
import math
import struct

ROOT = Path(__file__).resolve().parents[1]

def run(*args):
    subprocess.run(list(map(str, args)), cwd=ROOT, check=True)

def main():
    with tempfile.TemporaryDirectory() as tmp:
        work = Path(tmp)
        run('dotnet', 'run', '--project', 'tests/SystemFirmwareTests', '-c', 'Release',
            '-p:DndBuildNative=false', '--', work)
        binary = work / 'menu-audio'
        run(os.environ.get('CC', 'cc'), '-D_DARWIN_C_SOURCE', '-DDND_MENU_AUDIO_ONLY',
            '-std=c11', '-O1', '-Wall', '-Wextra', '-Werror', '-Wno-unused-function',
            '-Iinclude', 'tests/audio_formats_test.c', 'source/audio.c', '-lm', '-o', binary)
        run(binary, work)
        prepared=work/'prepared'
        run(os.environ.get('CC','cc'),'-D_DARWIN_C_SOURCE','-std=c11','-O1','-Wall','-Wextra','-Werror','-Wno-unused-function',
            '-Iinclude','tests/menu_recipe_test.c','source/audio.c','-lm','-o',prepared)
        run(prepared,work/'recipe.dme',work/'recipe.pcm')
        # Independent mathematical oracle for the authored sequence in
        # MenuRecipeTests: no source/recipe bytes are read to obtain expected
        # timing, envelopes, pitch, pan, or source samples.
        expected=[];unrounded=[];dsp_expected=[];gains=[];phase=dsp_phase=0;step=left=right=0
        for frame in range(6560):
            block=frame//80
            if frame%80==0:
                tick=math.floor(block*24000/32028.5)
                bend=math.floor(min(tick/4,1)*(.375)*64)/64
                step=math.floor(32000/32028.5*2**(bend/12)*4096)/4096
                def attack(age):
                    if age<2:return age/2
                    if age<5:return 1
                    return 1+(16384/32767-1)*min((age-5)/20,1)
                age=block*48000/32028.5;held=41*48000/32028.5
                env=attack(age) if block<41 else attack(held)*(1-(age-held)/60)
                gain=(90/128)*.5*env*(13107/16384)
                pan=.5*(1-min(tick/10,1))
                left=gain*math.cos(pan*math.pi/2);right=gain*math.sin(pan*math.pi/2)
            if phase>=17:phase=3+(phase-17)%14
            index=int(phase);following=index+1 if index+1<17 else 3
            a=(index-9)*1500;b=(following-9)*1500
            sample=a+(b-a)*(phase-index);phase+=step
            unrounded.extend((sample*left,sample*right))
            gains.append((left,right))
            expected.extend((int(sample*left),int(sample*right)))
            # Synthetic four-tap coefficient table selects tap 1 at unity
            # minus one Q15 unit. The DSP has four initial history samples.
            index=int(dsp_phase)-3;dsp_phase+=step
            if index<0:sample=0
            else:
                if index>=17:index=3+(index-17)%14
                sample=((index-9)*1500*32767)//32768
            dsp_expected.extend((int(sample*left),int(sample*right)))
        actual=struct.unpack('>'+str(6560*2)+'h',(work/'recipe.pcm').read_bytes())
        error=max(abs(a-b) for a,b in zip(actual,expected))
        assert error<=3,error
        print(f'Prepared PCM16 oracle: {6560} stereo frames; maximum error {error} LSB')
        run(prepared,work/'recipe-layered.dme',work/'layered.pcm')
        layered=list(expected);phase=0;step=4092/4096
        for frame in range(5440):
            block=frame//80
            env=1 if block<27 else 1-(block-27)*48000/32028.5/60
            gain=(90/128)*.25*(64/127)**2*env*(13107/16384)
            if phase>=17:phase=3+(phase-17)%14
            index=int(phase);following=index+1 if index+1<17 else 3
            sample=(index-9)*1500+(following-index)*1500*(phase-index)
            phase+=step;layered[frame*2]+=int(sample*gain)
        actual=struct.unpack('>'+str(6560*2)+'h',(work/'layered.pcm').read_bytes())
        error=max(abs(a-b) for a,b in zip(actual,layered))
        assert error<=3,error
        print(f'Layered PCM16 oracle: two independent sources; maximum error {error} LSB')
        run(prepared,work/'recipe-dsp.dme',work/'dsp.pcm')
        actual=struct.unpack('>'+str(6560*2)+'h',(work/'dsp.pcm').read_bytes())
        error=max(abs(a-b) for a,b in zip(actual,dsp_expected));assert error<=3,error
        print(f'Firmware-style four-tap resampler oracle: maximum error {error} LSB')
        run(prepared,work/'recipe-fast.dme',work/'fast.pcm')
        phase=0;fast=[]
        for frame in range(6560):
            if frame%80==0:
                tick=math.floor((frame//80)*24000/32028.5)
                bend=math.floor(min(tick/4,1)*.375*64)/64
                step=math.floor(32000/32028.5*2**((28+bend)/12)*4096)/4096
            phase+=step;index=int(phase)-4
            if index>=17:index=3+(index-17)%14
            sample=0 if index<0 else (index-9)*1500
            left,right=gains[frame];fast.extend((int(sample*left),int(sample*right)))
        actual=struct.unpack('>'+str(6560*2)+'h',(work/'fast.pcm').read_bytes())
        error=max(abs(a-b) for a,b in zip(actual,fast));assert error<=3,error
        print(f'High-ratio DSP selection oracle: maximum error {error} LSB')
        run(prepared,work/'recipe-wet.dme',work/'wet.pcm')
        wet=[];rings=[[0]*80,[0]*80];previous=[0,0];send=math.sin(127/512*math.pi/2)
        for frame in range(38560):
            for channel in range(2):
                dry=unrounded[frame*2+channel] if frame<6560 else 0
                old=rings[channel][frame%80];filtered=previous[channel]//2;previous[channel]=old
                rings[channel][frame%80]=filtered+int(dry*send)
                wet.append(int(dry+filtered*.25))
        actual=struct.unpack('>'+str(38560*2)+'h',(work/'wet.pcm').read_bytes())
        error=max(abs(a-b) for a,b in zip(actual,wet));assert error<=3,error
        print(f'Feedback FIR and complete-tail oracle: maximum error {error} LSB')

if __name__ == '__main__':
    main()
