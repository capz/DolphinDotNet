#!/usr/bin/env python3
"""Rebuild libbba from pinned source with a bounded TCP receive window."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile

REVISION='094b250f1e978d37789f18ffc2d3c9c2325062f8'

def run(*args,**kw):return subprocess.run(list(map(str,args)),check=True,**kw)

def prepare(source,destination,window):
    if not 2920<=window<=65535:raise ValueError('TCP window must be 2920..65535 bytes (unscaled 16-bit)')
    if subprocess.check_output(['git','-C',str(source),'rev-parse','HEAD'],text=True).strip()!=REVISION:
        raise ValueError('BBA source revision differs from pinned libogc2')
    if subprocess.check_output(['git','-C',str(source),'status','--porcelain','--untracked-files=no'],text=True).strip():
        raise ValueError('BBA source must be unmodified')
    shutil.copytree(source,destination,ignore=shutil.ignore_patterns('.git','build','lib','deps'))
    config=destination/'lwip/include/lwip/lwipopts.h';text=config.read_text()
    original='#define TCP_WND                 (2*TCP_MSS)'
    if text.count(original)!=1:raise ValueError('Review changed TCP configuration')
    config.write_text(text.replace(original,'#define TCP_WND                 '+str(window)))
    # Increasing the window does not enlarge the global receive pool or heap.
    for setting,value in [('PBUF_POOL_SIZE',128),('PBUF_POOL_BUFSIZE',1600),('MEMP_NUM_TCP_SEG',128)]:
        if not re.search(r'^#define\s+'+setting+r'\s+'+str(value)+r'\b',text,re.M):raise ValueError('Review changed BBA memory pool')
    return destination

def build(source,output,window,devkitpro,devkitppc):
    if not source.exists():
        run('git','clone','--quiet','--filter=blob:none','--no-checkout','https://github.com/extremscorner/libogc2.git',source)
        run('git','-C',source,'fetch','--quiet','--depth=1','origin',REVISION)
        run('git','-C',source,'checkout','--quiet','--detach',REVISION)
    output.mkdir(parents=True,exist_ok=True)
    identity={'revision':REVISION,'window_bytes':window,'pool_buffers':128,'pool_buffer_bytes':1600,
              'toolchain':subprocess.check_output([str(devkitppc/'bin/powerpc-eabi-gcc'),'--version'],text=True).splitlines()[0],
              'script_sha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest()}
    stamp=output/'build.json'
    if stamp.exists() and json.loads(stamp.read_text())==identity and (output/'libbba.a').exists():return
    with tempfile.TemporaryDirectory(prefix='bba-',dir=output.parent) as temporary:
        work=Path(temporary);tree=prepare(source,work/'source',window)
        make=(tree/'Makefile').read_text();section=re.search(r'^LWIPOBJ\s*:=\s*(.*?)(?=\n\s*\n)',make,re.M|re.S)
        if not section:raise ValueError('Missing pinned BBA translation units')
        modules=re.findall(r'\b(\w+)\.o',section[1]);objects=[]
        sdk=devkitpro/'libogc2/gamecube'
        includes=[tree,tree/'include',tree/'lwip/include',tree/'lwip/include/ipv4',tree/'lwip/include/netif',tree/'include/ogc',tree/'include/ogc/machine',sdk/'include']
        flags=['-O2','-g','-DLIBOGC_INTERNAL','-DGEKKO','-DHW_DOL','-mcpu=750','-meabi','-msdata=sysv','-mhard-float','-ffunction-sections','-fdata-sections','-fno-strict-aliasing',*['-I'+str(p) for p in includes]]
        for name in modules:
            matches=list((tree/'lwip').rglob(name+'.c'))
            if len(matches)!=1:raise ValueError('Ambiguous/missing BBA module '+name)
            target=work/(name+'.o');run(devkitppc/'bin/powerpc-eabi-gcc',*flags,'-c',matches[0],'-o',target);objects.append(target)
        run(devkitppc/'bin/powerpc-eabi-ar','rcs',work/'libbba.a',*objects)
        (work/'build.json').write_text(json.dumps(identity,indent=2)+'\n')
        os.replace(work/'libbba.a',output/'libbba.a');os.replace(work/'build.json',stamp)
    print('DND_BBA_OVERLAY_READY window='+str(window))

def main():
    root=Path(__file__).resolve().parents[1];p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--window',type=int,required=True);p.add_argument('--source',type=Path,default=root/'build/bba-upstream');p.add_argument('--output',type=Path,default=root/'build/bba-overlay')
    a=p.parse_args()
    if not 2920<=a.window<=65535:p.error('window must be 2920..65535')
    build(a.source.resolve(),a.output.resolve(),a.window,Path(os.environ['DEVKITPRO']),Path(os.environ['DEVKITPPC']))
if __name__=='__main__':main()
