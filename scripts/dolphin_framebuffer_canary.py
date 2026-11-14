#!/usr/bin/env python3
"""Qualify GX XFB copy bounds using the optional native red-zone fixture.

Build tests/framebuffer_canary_gamecube.c with the SDK EXI overlay and
-Wl,--wrap=SYS_AllocateFramebuffer, preserving a matching DOL/ELF pair.
Run under the repository's Linux Xvfb/Dolphin setup; no original IPL is needed.
This is emulator GPU evidence, not physical-console qualification.
"""
import argparse
import json
from pathlib import Path
import socket
import struct
import subprocess
import time

from dolphin_controller_test import request
from dolphin_gdb_probe import parse_stop_pc


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dol',type=Path,required=True)
    parser.add_argument('--elf',type=Path,required=True)
    parser.add_argument('--logs',type=Path,required=True)
    parser.add_argument('--dolphin',default='/usr/games/dolphin-emu-nogui')
    parser.add_argument('--port',type=int,default=7176)
    args=parser.parse_args()
    root=args.logs.resolve()
    if root.exists() and any(root.iterdir()):parser.error('Use a fresh log directory')
    root.mkdir(parents=True,exist_ok=True);config=root/'user/Config';config.mkdir(parents=True)
    (config/'Dolphin.ini').write_text('[Core]\nGFXBackend = OGL\nCPUCore = 1\nCPUThread = False\n[General]\nGDBPort = '+str(args.port)+'\n[Interface]\nDebugModeEnabled = True\n[DSP]\nBackend = No Audio Output\n[Display]\nRenderWindowWidth = 640\nRenderWindowHeight = 480\n')
    (config/'GFX.ini').write_text('[Settings]\nInternalResolution = 1\nMSAA = 1\nSSAA = False\n[Enhancements]\nPostProcessingShader = \n[Hacks]\nXFBToTextureEnable = False\nDeferEFBCopies = False\nForceProgressive = False\n')
    wanted={'dnd_framebuffer_canary_done','dnd_framebuffer_canary_rows','dnd_framebuffer_canary_stage'};symbols={}
    for line in subprocess.check_output(['nm','-a',str(args.elf.resolve())],text=True).splitlines():
        parts=line.split()
        if len(parts)==3 and parts[2] in wanted:symbols[parts[2]]=int(parts[0],16)
    if symbols.keys()!=wanted:raise ValueError('Missing native canary symbols')
    log=(root/'emulator.log').open('w')
    process=subprocess.Popen([args.dolphin,'--platform','x11','-u',str(root/'user'),'-C','SYSCONF.IPL.PGS=True','-e',str(args.dol.resolve())],stdout=log,stderr=subprocess.STDOUT)
    connection=None
    try:
        deadline=time.monotonic()+30
        while time.monotonic()<deadline:
            if process.poll() is not None:raise RuntimeError('Dolphin exited; inspect emulator.log')
            try:connection=socket.create_connection(('127.0.0.1',args.port),timeout=.3);break
            except OSError:time.sleep(.1)
        if connection is None:raise TimeoutError('No native canary debugger')
        connection.settimeout(60);address=symbols['dnd_framebuffer_canary_done']
        if request(connection,f'Z0,{address:x},4')!='OK':raise RuntimeError('Cannot install completion breakpoint')
        stop=request(connection,'c',60)
        if parse_stop_pc(stop)!=address:raise RuntimeError('Unexpected native completion stop: '+stop)
        stage=int(request(connection,f"m{symbols['dnd_framebuffer_canary_stage']:x},4"),16)
        if stage!=12:raise RuntimeError('Native canary failed before completion, stage='+hex(stage))
        raw=bytes.fromhex(request(connection,f"m{symbols['dnd_framebuffer_canary_rows']:x},{12*36:x}"))
        if len(raw)!=12*36:raise ValueError('Incomplete native canary rows')
        (root/'rows.bin').write_bytes(raw)
        names=['240p/helper','480i/helper','480p/helper','480i-AA/legacy','480i-AA/helper','480p-AA/legacy','480p-AA/helper','576i/helper','576p/helper','480p-AA/return','240p-AA/helper','480i/safe-return']
        fields=['width','efb_rows','vi_rows','predicted_rows','storage_rows','claimed_bytes','last_written_byte','red_zone_changed_bytes','legacy']
        rows=[dict(mode=name,**dict(zip(fields,struct.unpack_from('>9I',raw,i*36)))) for i,name in enumerate(names)]
        # The unsafe controls still have a separately allocated red zone. A
        # changed control proves real DMA happened beyond the old claimed size.
        controls=all(r['red_zone_changed_bytes']>0 and r['last_written_byte']>r['claimed_bytes'] for r in rows if r['legacy'])
        protected=all(r['last_written_byte']>0 and r['last_written_byte']<=r['claimed_bytes'] and r['red_zone_changed_bytes']==0 for r in rows if not r['legacy'])
        result={'success':controls and protected,'scope':'Native PowerPC fixture; actual Dolphin OpenGL GX_CopyDisp writes, no emulator AA or postprocessing; not physical hardware','completion_pc':hex(address),'legacy_controls_overwrite_red_zone':controls,'production_helper_preserves_red_zone':protected,'rows':rows}
        (root/'result.json').write_text(json.dumps(result,indent=2)+'\n');print(json.dumps(result,indent=2),flush=True)
        if not result['success']:raise RuntimeError('Native framebuffer canary failed; inspect result.json')
    finally:
        if connection is not None:connection.close()
        process.terminate()
        try:process.wait(timeout=10)
        except subprocess.TimeoutExpired:process.kill();process.wait()
        log.close()


if __name__=='__main__':main()
