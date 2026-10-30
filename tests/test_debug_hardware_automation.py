"""Offline, device-free validation of the real GDB hardware-automation API."""
import hashlib
import io
import json
import struct
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0,str(Path(__file__).resolve().parents[1]/"scripts"))
import debug_hardware_automation as hw


class PacketStream:
    def __init__(self):
        self.incoming=bytearray()
        self.writes=[]
    def write(self,data):
        self.writes.append(data)
        if data.startswith(b"$"):
            payload=data[1:data.index(b"#")]
            if payload.startswith(b"m"):reply=b"01020304"
            elif payload.startswith(b"M"):reply=b"OK"
            else:reply=b"S05"
            self.incoming.extend(b"+$"+reply+b"#"+("%02x"%(sum(reply)&255)).encode())
        return len(data)
    def flush(self):pass
    def read(self,count):
        if not self.incoming:return b""
        out=bytes(self.incoming[:count]);del self.incoming[:count];return out


class FakeTarget:
    def __init__(self):
        self.addr=0x80001000
        self.hook=0x80001200
        self.mail=bytearray(0x60)
        self.mail[0:8]=hw.pack(hw.MAGIC,hw.VERSION)
        self.picture=bytes([16,128,16,128,235,128,235,128]*2)
        self.stops=[]
        self.bps=[]
        self.input_commands=0
    def get(self,offset):
        return struct.unpack_from(">I",self.mail,offset)[0]
    def put(self,offset,value):
        struct.pack_into(">I",self.mail,offset,value)
    def memory(self,addr,count):
        if self.addr<=addr<self.addr+0x60:
            off=addr-self.addr
            return bytes(self.mail[off:off+count])
        if addr==0xc0120000:
            return self.picture
        raise IOError("unexpected address")
    def write(self,addr,data):
        off=addr-self.addr
        self.mail[off:off+len(data)]=data
    def breakpoint(self,addr,enable=True):
        self.bps.append((addr,enable))
    def stop(self,operation):
        self.stops.append(operation)
        if operation=="c":
            self.put(0x44,self.get(0x44)+1)
            self.put(0x40,self.get(0x40)+1)
            self.put(0x48,0xc0120000)
            self.put(0x4c,4)
            self.put(0x50,2)
            self.put(0x54,8)
            self.put(0x58,16)
            self.put(0x5c,1)
            seq=self.get(0x08)
            if seq!=self.get(0x0c):
                self.put(0x0c,seq)
                self.put(0x10,1)
                self.input_commands+=1
        return "S05"


class HardwareAutomationTests(unittest.TestCase):
    def test_rsp_packet_and_memory_write_bounds(self):
        stream=PacketStream()
        rsp=hw.Rsp(stream)
        self.assertEqual(rsp.memory(0x80000000,4),b"\x01\x02\x03\x04")
        rsp.write(0x80000200,b"\x00\x01\xfe\xff")
        self.assertEqual(rsp.stop("s"),"S05")
        self.assertIn(b"$M80000200,4:0001feff#",stream.writes)
        with self.assertRaises(ValueError):rsp.write(0x80000000,b"")
        with self.assertRaises(ValueError):rsp.memory(0x80000000,1048577)

    def test_yuyv_to_rgb_and_png(self):
        pixels=hw.yuyv_to_rgb(bytes([16,128,235,128,16,128,235,128]),2,2,4)
        self.assertEqual(pixels,bytes([0,0,0,255,255,255]*2))
        with self.assertRaises(ValueError):
            hw.yuyv_to_rgb(b"short",4,4,8)
        with tempfile.TemporaryDirectory() as td:
            path=Path(td)/"frame.png"
            hw.save_png(path,pixels,2,2)
            data=path.read_bytes()
            self.assertTrue(data.startswith(b"\x89PNG\r\n\x1a\n"))
            self.assertEqual(struct.unpack_from(">II",data,16),(2,2))

    def test_full_script_and_golden_hash(self):
        fake=FakeTarget()
        session=hw.HardwareSession(fake,fake.addr,fake.hook)
        session.begin()
        self.assertEqual(session.last_frame,1)
        expected=hashlib.sha256(fake.picture).hexdigest()
        with tempfile.TemporaryDirectory() as td:
            records=hw.run_sequence(session,[{
                "port":0,"buttons":["A","START"],"stick":[80,0],
                "polls":2,"frames":2,"capture":"test-frame.png","sha256":expected
            },{
                "clear":True,"port":4,"frames":1
            }],Path(td))
            self.assertEqual(len(records),2)
            self.assertEqual(records[0]["width"],4)
            self.assertEqual(records[0]["height"],2)
            self.assertEqual(fake.input_commands,2)
            self.assertEqual(fake.bps.count((fake.hook,False)),3)
            self.assertEqual(fake.bps.count((fake.hook,True)),4)
            self.assertTrue((Path(td)/"test-frame.png").is_file())
        self.assertEqual(fake.stops,["c","s","c","s","c","s","c"])

    def test_rejects_invalid_step(self):
        fake=FakeTarget()
        session=hw.HardwareSession(fake,fake.addr,fake.hook)
        session.begin()
        with tempfile.TemporaryDirectory() as td:
            for step in ({"buttons":["SELECT"]},
                         {"stick":[128,0]},
                         {"frames":0},
                         {"port":9}):
                with self.assertRaises(ValueError):
                    hw.run_sequence(session,[step],Path(td))
            with self.assertRaises(ValueError):
                hw.run_sequence(session,[{"capture":"../escape.png"}],Path(td))
            self.assertFalse((Path(td).parent/"escape.png").exists())

    def test_rejects_unsupported_mailbox_version(self):
        fake=FakeTarget()
        fake.put(4,2)
        with self.assertRaises(RuntimeError):
            hw.HardwareSession(fake,fake.addr,fake.hook).begin()


if __name__=="__main__":
    unittest.main()
