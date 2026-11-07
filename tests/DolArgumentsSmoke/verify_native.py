#!/usr/bin/env python3
"""Run the real managed caller -> PPC DOL handoff in cloud-local Dolphin/GDB.
No timing result, game compatibility or physical hardware result is inferred.
"""
import argparse,hashlib,json,socket,subprocess,time
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--caller',type=Path,required=True);p.add_argument('--target-elf',type=Path,required=True);p.add_argument('--logs',type=Path,required=True);a=p.parse_args()
root=a.logs.resolve();cfg=root/'user/Config';cfg.mkdir(parents=True)
(cfg/'Dolphin.ini').write_text('[Core]\nGFXBackend = OGL\nCPUCore = 1\nCPUThread = False\nEmulationSpeed = 1.0\n[DSP]\nBackend = No Audio Output\n[General]\nGDBPort = 2174\n')
table=subprocess.run(['nm','-a',str(a.target_elf)],capture_output=True,text=True,check=True).stdout
symbols={}
for line in table.splitlines():
 fields=line.split()
 if len(fields)==3 and fields[2] in ['argv_result','argv_count','argv_length','argv_capture']:symbols[fields[2]]=int(fields[0],16)
assert len(symbols)==4
expected=['swiss-gc.dol','Autoload=gcldr:/games/Fixture Game.iso','AutoBoot=Yes','Force PAL Video Mode=576p','caption=ü']
raw=b''.join(s.encode('utf-8')+b'\0' for s in expected)
def send(sock,s):
 data=s.encode();sock.sendall(b'$'+data+b'#'+f'{sum(data)&255:02x}'.encode())
def reply(sock):
 while True:
  ch=sock.recv(1)
  if not ch:raise EOFError('Debugger disconnected')
  if ch==b'$':break
  if ch not in (b'+',b'-'):raise ValueError('Unexpected GDB framing')
 data=b''
 while True:
  ch=sock.recv(1)
  if not ch:raise EOFError()
  if ch==b'#':break
  data+=ch
 checksum=sock.recv(2);assert int(checksum,16)==sum(data)&255;sock.sendall(b'+');return data.decode()
def memory(sock,address,count):
 send(sock,f'm{address:x},{count:x}');r=reply(sock)
 while r=='' or r.startswith(('T','S')):r=reply(sock)
 data=bytes.fromhex(r)
 if len(data)!=count:raise ValueError('Truncated GDB read')
 return data
with (root/'emulator.log').open('w') as log:
 proc=subprocess.Popen(['/usr/games/dolphin-emu-nogui','--platform','x11','-u',str(root/'user'),'-e',str(a.caller)],stdout=log,stderr=subprocess.STDOUT)
 try:
  deadline=time.monotonic()+30;sock=None
  while time.monotonic()<deadline:
   try:sock=socket.create_connection(('127.0.0.1',2174),timeout=.3);break
   except OSError:time.sleep(.1)
  if sock is None:raise TimeoutError('No debugger')
  with sock:
   sock.settimeout(10);memory(sock,0x80000000,4);send(sock,'c');time.sleep(15);sock.sendall(b'\x03');r=reply(sock)
   while not r.startswith(('T','S')):r=reply(sock)
   send(sock,'p40');registers=reply(sock)
   while registers=='' or registers.startswith(('T','S')):registers=reply(sock)
   (root/'pc.txt').write_text(registers+'\n')
   result=int.from_bytes(memory(sock,symbols['argv_result'],4),'big');count=int.from_bytes(memory(sock,symbols['argv_count'],4),'big');length=int.from_bytes(memory(sock,symbols['argv_length'],4),'big')
   captured=memory(sock,symbols['argv_capture'],min(length,512))
   diagnostics={}
   if result!=0x41524756:
    caller_elf=a.caller.with_suffix('.elf')
    if caller_elf.exists():
     for line in subprocess.run(['nm','-a',str(caller_elf)],capture_output=True,text=True,check=True).stdout.splitlines():
      fields=line.split()
      if len(fields)==3 and fields[2].split('.lto_priv.')[0] in ['exception_is_pending','exception_kind','exception_text','exception_object']:
       value=int.from_bytes(memory(sock,int(fields[0],16),4),'big');diagnostics[fields[2]]=hex(value)
       if fields[2].startswith('exception_text') and 0x80000000<=value<0x81800000:diagnostics['error_text']=memory(sock,value,200).split(b'\0')[0].decode('ascii',errors='replace')
   report={'diagnostics':diagnostics,'environment':'Real PPC GameCube DOL chainload in cloud Dolphin 2503; not physical hardware','caller_sha256':hashlib.sha256(a.caller.read_bytes()).hexdigest(),'magic':hex(result),'count':count,'length':length,'captured_hex':captured.hex(),'expected':expected,'passed':result==0x41524756 and count==len(expected) and captured==raw}
   (root/'result.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report))
   assert report['passed'],'Native target did not receive the exact UTF-8/NUL-delimited arguments'
 finally:
  proc.terminate()
  try:proc.wait(timeout=5)
  except subprocess.TimeoutExpired:proc.kill();proc.wait()
