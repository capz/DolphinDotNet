#!/usr/bin/env python3
"""Run only against fresh isolated Dolphin memory cards, never physical hardware."""
import argparse,hashlib,json,socket,subprocess,sys,time
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'scripts'))
from dolphin_controller_test import memory,request
from dolphin_gdb_probe import _send,_reply
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--dol',type=Path,required=True);p.add_argument('--logs',type=Path,required=True);a=p.parse_args()
root=a.logs.resolve();root.mkdir(parents=True,exist_ok=False);config=root/'user/Config';config.mkdir(parents=True)
(config/'Dolphin.ini').write_text('[Core]\nGFXBackend = OGL\nCPUCore = 1\nCPUThread = False\nSlotA = 1\nSlotB = 1\n[DSP]\nBackend = No Audio Output\n[General]\nGDBPort = 2174\n')
syms={}
for line in subprocess.run(['nm','-a',str(a.dol.with_suffix('.elf'))],capture_output=True,text=True,check=True).stdout.splitlines():
 f=line.split()
 if len(f)==3:
  for name in ['Result','Steps','Status','Probe']:
   if f[2].split('.lto_priv.')[0]=='dnd_static_Program_'+name:syms[name]=int(f[0],16)
  if f[2].split('.lto_priv.')[0]=='exception_text':syms['NativeError']=int(f[0],16)
assert set(["Result","Steps","Status","Probe"])<=syms.keys(),syms
with (root/'emulator.log').open('w') as log:
 proc=subprocess.Popen(['/usr/games/dolphin-emu-nogui','--platform','x11','-u',str(root/'user'),'-e',str(a.dol.resolve())],stdout=log,stderr=subprocess.STDOUT)
 try:
  conn=None;deadline=time.monotonic()+30
  while time.monotonic()<deadline:
   if proc.poll() is not None:raise RuntimeError('Dolphin exited')
   try:conn=socket.create_connection(('127.0.0.1',2174),timeout=.3);break
   except OSError:time.sleep(.1)
  if conn is None:raise TimeoutError('No debugger')
  with conn:
   memory(conn,syms['Result'],4);_send(conn,'c');time.sleep(30);conn.sendall(b'\x03');reply=_reply(conn,time.monotonic()+10)
   while not reply.startswith(('T','S')):reply=_reply(conn,time.monotonic()+10)
   result_array=int.from_bytes(memory(conn,syms['Result'],4),'big');result=int.from_bytes(memory(conn,result_array+24,4),'big',signed=True);steps=int.from_bytes(memory(conn,syms['Steps'],4),'big');address=int.from_bytes(memory(conn,syms['Status'],4),'big');length=int.from_bytes(memory(conn,address+8,4),'big') if address else 0;assert length<512
   status=memory(conn,address+12,length*2).decode('utf-16-be') if address else '(null managed message)'
   native_error=''
   if 'NativeError' in syms:
    text_pointer=int.from_bytes(memory(conn,syms['NativeError'],4),'big')
    if text_pointer:native_error=memory(conn,text_pointer,200).split(b'\0')[0].decode('ascii',errors='replace')
   probe_array=int.from_bytes(memory(conn,syms['Probe'],4),'big');probes=[int.from_bytes(memory(conn,probe_array+24+i*4,4),'big',signed=True) for i in range(2)]
   phase=int.from_bytes(memory(conn,result_array+28,4),'big');report={'native_error':native_error,'phase':phase,'initial_mount_results':probes,'environment':'Dolphin 2503 with two fresh isolated emulated memory cards; not physical hardware','dol_sha256':hashlib.sha256(a.dol.read_bytes()).hexdigest(),'result':result,'steps':steps,'status':status,'passed':result==1}
   (root/'result.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report));assert report['passed'],status
 finally:
  proc.terminate()
  try:proc.wait(timeout=5)
  except subprocess.TimeoutExpired:proc.kill();proc.wait()
