#!/usr/bin/env python3
"""Hardware GameCube regression runner over the existing GDB RSP debugger.

The target must be a GameCube Debug ELF with DND_DEBUG_AUTOMATION enabled.
Exclusive RSP ownership: do not simultaneously attach Rider / Visual Studio.
No arbitrary memory writes: only the named, versioned runtime test mailbox.
"""
import argparse
import hashlib
import json
import re
import shutil
import socket
import struct
import subprocess
import sys
import zlib
from pathlib import Path

MAGIC = 0x444e4441
VERSION = 1
MAILBOX_SIZE = 0x60
BUTTONS = {
    "LEFT": 0x0001, "RIGHT": 0x0002, "DOWN": 0x0004, "UP": 0x0008,
    "Z": 0x0010, "R": 0x0020, "L": 0x0040,
    "A": 0x0100, "B": 0x0200, "X": 0x0400, "Y": 0x0800, "START": 0x1000
}

def pack(*ints):
    return struct.pack(">" + "I" * len(ints), *(x & 0xffffffff for x in ints))

def unpack(data):
    if len(data) % 4:
        raise ValueError("Invalid mailbox size.")
    return struct.unpack(">" + "I" * (len(data)//4), data)

def symbols(elf):
    """Locate ELF symbols, not source line mappings, using an installed binutils nm."""
    nm = shutil.which("powerpc-eabi-nm") or shutil.which("nm")
    if not nm:
        raise RuntimeError("Install devkitPPC binutils (powerpc-eabi-nm) or nm.")
    proc = subprocess.run([nm, "-g", "--defined-only", str(elf)],
                          capture_output=True, text=True, check=True)
    result = {}
    for line in proc.stdout.splitlines():
        match = re.fullmatch(r"\s*([0-9A-Fa-f]{8,16})\s+[A-Za-z]\s+(\S+)\s*", line)
        if match:
            result[match.group(2)] = int(match.group(1), 16)
    names = ("dnd_automation_mailbox", "dnd_automation_frame_boundary")
    if not all(n in result for n in names):
        raise RuntimeError("ELF does not export hardware automation symbols. "
                           "Build GameCube Debug with DndDebugSymbols=true.")
    return result[names[0]], result[names[1]]

class Rsp:
    """Small synchronous RSP client; m/M chunks never exceed the 512-byte libdb limit."""
    def __init__(self, stream):
        self.stream = stream

    def read_exact(self, count):
        chunks = bytearray()
        while len(chunks) < count:
            value = self.stream.read(count - len(chunks))
            if not value:
                raise TimeoutError("GDB RSP target stopped responding.")
            chunks.extend(value)
        return bytes(chunks)

    def command(self, payload):
        wire = payload.encode("ascii")
        checksum = sum(wire) & 0xff
        self.stream.write(b"$" + wire + b"#" + f"{checksum:02x}".encode("ascii"))
        self.stream.flush()
        ack = self.read_exact(1)
        if ack != b"+":
            raise IOError("GDB did not ACK command (received %r)" % ack)
        if payload == "c" or payload == "s":
            # All-stop RSP: a resumed target replies only at a breakpoint/step.
            pass
        while True:
            if self.read_exact(1) != b"$":
                raise IOError("Missing GDB reply frame.")
            response = bytearray()
            while True:
                item = self.read_exact(1)
                if item == b"#":
                    break
                if len(response) > 16384:
                    raise IOError("Oversized RSP reply.")
                response.extend(item)
            remote_checksum = int(self.read_exact(2), 16)
            if remote_checksum != sum(response) & 0xff:
                self.stream.write(b"-")
                self.stream.flush()
                raise IOError("Invalid RSP reply checksum.")
            self.stream.write(b"+")
            self.stream.flush()
            # GDB can send asynchronous console-output packets before the
            # actual stop/memory response. Acknowledge and drain them.
            if len(response)>1 and response[0] == ord("O") and \
               len(response[1:]) % 2 == 0 and \
               all(ch in b"0123456789abcdefABCDEF" for ch in response[1:]):
                continue
            return response.decode("ascii")

    def memory(self, addr, count):
        if not 0 <= count <= 1024*1024:
            raise ValueError("Memory read must fit the bounded XFB transfer.")
        out = bytearray()
        while len(out) < count:
            block = min(512, count-len(out))
            answer = self.command(f"m{addr+len(out):08x},{block:x}")
            if answer.startswith("E") or len(answer) != 2*block:
                raise IOError("Target rejected memory read at 0x%08x: %s" % (addr+len(out), answer))
            try:
                out.extend(bytes.fromhex(answer))
            except ValueError as exc:
                raise IOError("Malformed GDB memory reply.") from exc
        return bytes(out)

    def write(self, addr, data):
        if not 1 <= len(data) <= 512:
            raise ValueError("RSP write must be between 1 and 512 bytes.")
        answer = self.command(f"M{addr:08x},{len(data):x}:{data.hex()}")
        if answer != "OK":
            raise IOError("Target rejected memory write: "+answer)

    def breakpoint(self, addr, enable=True):
        if not addr or addr & 3:
            raise ValueError("PowerPC breakpoints must be four-byte aligned.")
        op = "Z0" if enable else "z0"
        if self.command(f"{op},{addr:08x},4") != "OK":
            raise IOError("Could not %s frame breakpoint." % ("set" if enable else "clear"))

    def stop(self, operation):
        value = self.command(operation)
        if not value or value[0] not in "ST":
            raise IOError("Unexpected GDB stop response: "+value)
        return value

def yuyv_to_rgb(source, width, height, stride):
    """BT.601 limited-range XFB Y0-U-Y1-V -> RGB24 (one full native pixel per output pixel)."""
    if width <= 0 or width > 720 or width % 2 or height <= 0 or height > 576:
        raise ValueError("Invalid XFB dimensions.")
    if stride != width * 2 or len(source) != stride*height:
        raise ValueError("Invalid XFB stride or length.")
    out = bytearray(width*height*3)
    def clip(x):
        return 0 if x < 0 else (255 if x > 255 else x)
    for y in range(height):
        for x in range(0, width, 2):
            idx = y*stride+x*2
            y0,u,y1,v = source[idx:idx+4]
            for dx,lum in ((0,y0),(1,y1)):
                c=max(0,lum-16)
                d=u-128
                e=v-128
                at=(y*width+x+dx)*3
                out[at] = clip((298*c+409*e+128)>>8)
                out[at+1] = clip((298*c-100*d-208*e+128)>>8)
                out[at+2] = clip((298*c+516*d+128)>>8)
    return bytes(out)

def save_png(path, rgb, width, height):
    if len(rgb)!=width*height*3:
        raise ValueError("RGB capture length mismatch.")
    def chunk(kind, data):
        return pack_png(len(data))+kind+data+pack_png(zlib.crc32(kind+data))
    def pack_png(value):
        return struct.pack(">I", value & 0xffffffff)
    scanlines = b"".join(b"\x00"+rgb[row*width*3:(row+1)*width*3]
                         for row in range(height))
    output = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB",width,height,8,2,0,0,0))
    output += chunk(b"IDAT",zlib.compress(scanlines,6))+chunk(b"IEND",b"")
    Path(path).parent.mkdir(parents=True,exist_ok=True)
    Path(path).write_bytes(output)

class HardwareSession:
    """A step = one physical presentation boundary; injection lifetime = PAD polls."""
    def __init__(self, rsp, mailbox_address, boundary_address):
        self.rsp=rsp
        self.mailbox_address=mailbox_address
        self.boundary_address=boundary_address
        self.sequence=0
        self.last_frame=None

    def state(self):
        state=unpack(self.rsp.memory(self.mailbox_address,MAILBOX_SIZE))
        if state[0]!=MAGIC or state[1]!=VERSION:
            raise RuntimeError("Debugger attached to incompatible automation mailbox.")
        return state

    def begin(self):
        # Confirm the target is stopped before reading or patching its memory.
        self.rsp.stop("?")
        self.state()
        self.rsp.breakpoint(self.boundary_address)
        self.rsp.stop("c")
        self.last_frame=self.state()[17]
        if not self.last_frame:
            raise RuntimeError("Did not reach a presented GX frame.")

    def frame(self):
        # Step over the breakpoint instruction with it temporarily disarmed;
        # this avoids repeated stops at the same PowerPC PC on libdb stubs.
        self.rsp.breakpoint(self.boundary_address,False)
        try:
            self.rsp.stop("s")
        finally:
            self.rsp.breakpoint(self.boundary_address,True)
        self.rsp.stop("c")
        state=self.state()
        if state[17] == self.last_frame:
            raise RuntimeError("Frame-boundary breakpoint did not advance.")
        self.last_frame=state[17]
        return state

    def frames(self, count):
        if not 1 <= count <= 3600:
            raise ValueError("Frame count must be 1..3600.")
        for _ in range(count):
            self.frame()
        return self.state()

    def command(self, port, *, buttons=0, polls=1, connected=True,
                stick=(0,0), cstick=(0,0), triggers=(0,0), clear=False):
        if not 0 <= port <= (4 if clear else 3):
            raise ValueError("Port must be 0..3 (or 4 to clear all).")
        if not 1 <= polls <= 3600:
            raise ValueError("Injected poll duration must be 1..3600.")
        if buttons & ~0x1f7f or buttons < 0:
            raise ValueError("Invalid GameCube PAD button mask.")
        axes=(*stick,*cstick)
        if len(axes)!=4 or any(not -127<=a<=127 for a in axes):
            raise ValueError("Invalid GameCube stick values.")
        if len(triggers)!=2 or any(not 0<=t<=255 for t in triggers):
            raise ValueError("Invalid trigger travel.")
        self.sequence=(max(self.sequence,self.state()[3])+1)&0xffffffff
        if self.sequence==0:self.sequence=1
        # +14 to +3c are command fields. Commit request_seq (+08) last.
        self.rsp.write(self.mailbox_address+0x14,pack(2 if clear else 1,port,polls,
                       int(bool(connected)),buttons,*axes,*triggers))
        self.rsp.write(self.mailbox_address+0x08,pack(self.sequence))

    def verify_ack(self, state):
        if state[3]!=self.sequence or state[4]!=1:
            raise RuntimeError("Native input injection was rejected or not polled: "
                               "request=%d ack=%d status=%d" %
                               (self.sequence,state[3],state[4]))

    def capture(self, path):
        s=self.state()
        _,_,_,_,_,_,_,_,_,_,_,_,_,_,_,_,_,frame,addr,w,h,stride,length,format_=s
        if not frame or frame!=self.last_frame or not addr or format_!=1 or \
            not 2<=w<=720 or w%2 or not 1<=h<=576 or stride!=w*2 or \
            length!=stride*h or length>1024*1024:
            raise RuntimeError("Native XFB capture metadata is invalid or not ready.")
        raw=self.rsp.memory(addr,length)
        rgb=yuyv_to_rgb(raw,w,h,stride)
        save_png(path,rgb,w,h)
        return {"frame":frame,"width":w,"height":h,"sha256":hashlib.sha256(raw).hexdigest(),
                "rawBytes":len(raw),"png":str(path)}

def run_sequence(session,steps,output_dir):
    records=[]
    for index,step in enumerate(steps):
        if not isinstance(step,dict):
            raise ValueError("Each automation step must be an object.")
        count=step.get("frames",1)
        if type(count)!=int:
            raise ValueError("frames must be an integer.")
        port=step.get("port",0)
        if type(port)!=int:
            raise ValueError("port must be an integer.")
        presses=step.get("buttons",[])
        if not isinstance(presses,list):
            raise ValueError("buttons must be an array.")
        names=[x.upper() for x in presses if isinstance(x,str)]
        if len(names)!=len(presses) or any(x not in BUTTONS for x in names):
            raise ValueError("Unknown GameCube button.")
        buttons=0
        for name in names:buttons|=BUTTONS[name]
        clear=step.get("clear",False)
        session.command(port,buttons=buttons,polls=step.get("polls",count),
                        connected=step.get("connected",True),clear=clear,
                        stick=tuple(step.get("stick",[0,0])),
                        cstick=tuple(step.get("cstick",[0,0])),
                        triggers=tuple(step.get("triggers",[0,0])))
        before=session.state()[16]
        state=session.frames(count)
        session.verify_ack(state)
        if state[16] == before:
            raise RuntimeError("Game did not poll controller input during test step.")
        filename=step.get("capture",f"frame-{index:03d}.png")
        if not isinstance(filename,str) or filename!=Path(filename).name or \
           not filename.endswith(".png"):
            raise ValueError("Capture name must be a basename ending in .png.")
        result=session.capture(output_dir/filename)
        expected=step.get("sha256")
        if expected is not None and result["sha256"].lower()!=str(expected).lower():
            raise AssertionError("Frame %d sha256 mismatch: got %s expected %s" %
                                 (index,result["sha256"],expected))
        records.append({"step":index,"inputPolls":state[16]-before,**result})
    return records

def main(argv=None):
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--elf",type=Path,required=True)
    conn=parser.add_mutually_exclusive_group(required=True)
    conn.add_argument("--tcp",help="RSP host:port, e.g. 127.0.0.1:2828")
    conn.add_argument("--serial",help="USB Gecko COM/path (requires pyserial)")
    parser.add_argument("--baud",type=int,default=115200)
    parser.add_argument("--timeout",type=float,default=30)
    parser.add_argument("--script",type=Path,required=True,help="JSON array of input/frame assertions")
    parser.add_argument("--output",type=Path,required=True)
    args=parser.parse_args(argv)
    if not 1 <= args.timeout <= 600 or not 1 <= args.baud <= 3000000:
        parser.error("Invalid timeout or baud rate.")
    mailbox,boundary=symbols(args.elf)
    if args.tcp:
        host,separator,port=args.tcp.rpartition(":")
        if not separator or not host or not port.isdecimal() or not 1<=int(port)<=65535:
            parser.error("--tcp requires host:port.")
        sock=socket.create_connection((host,int(port)),timeout=args.timeout)
        sock.settimeout(args.timeout)
        stream=sock.makefile("rwb",buffering=0)
    else:
        try:
            import serial
        except ImportError as exc:
            raise RuntimeError("USB Gecko support requires pip install pyserial.") from exc
        stream=serial.Serial(args.serial,baudrate=args.baud,timeout=args.timeout,
                             write_timeout=args.timeout)
    try:
        steps=json.loads(args.script.read_text(encoding="utf-8"))
        if not isinstance(steps,list) or not 1<=len(steps)<=100:
            raise ValueError("Script must be a JSON array with 1..100 steps.")
        session=HardwareSession(Rsp(stream),mailbox,boundary)
        session.begin()
        records=run_sequence(session,steps,args.output)
        args.output.mkdir(parents=True,exist_ok=True)
        (args.output/"results.json").write_text(json.dumps(records,indent=2)+"\n",encoding="utf-8")
        print(json.dumps({"passed":len(records),"results":records},indent=2))
    finally:
        stream.close()
        if args.tcp:sock.close()

if __name__=="__main__":
    try:main()
    except (IOError,ValueError,RuntimeError,TimeoutError,AssertionError) as error:
        print("Hardware automation failed: "+str(error),file=sys.stderr)
        sys.exit(1)
