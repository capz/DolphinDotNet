"""Independent host fixture-contract checks; no proprietary game bytes needed."""
import json
import struct
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0,str(Path(__file__).resolve().parents[1]/"scripts"))
import jsystem_hardware_regression as js

class FakeRsp:
    def __init__(self,count=2,phase=3):
        self.count=count
        self.phase=phase
        self.selected=0
        self.serial=1
    def memory(self,addr,size):
        assert size==248 and addr==0x80004000
        name=("demo.iso/dvd:/assets/texture%d.bti"%self.selected).encode("ascii")
        fields=[js.FIXTURE_MAGIC,1,self.serial,self.phase,0,1,
                self.selected,self.count,self.selected+1,0,0,10,1,len(name)]
        return struct.pack(">14I",*fields)+name+b"\0"*(192-len(name))

class FakeSession:
    def __init__(self,count=2,phase=3):
        self.rsp=FakeRsp(count,phase)
        self.commands=[]
        self.pending=0
    def capture(self,path):
        path.parent.mkdir(parents=True,exist_ok=True)
        path.write_bytes(b"fake-png-test-bytes")
        return dict(frame=self.rsp.selected+1,width=640,height=480,
                    sha256=("0"*63+str(self.rsp.selected)),rawBytes=614400,png=str(path))
    def command(self,port,*,buttons,polls,connected):
        self.commands.append((port,buttons,polls,connected))
        self.pending=buttons
    def frames(self,count):
        if self.pending==js.BUTTONS["A"]:
            self.rsp.selected+=1
        self.rsp.serial+=1
        return [0]*24
    def verify_ack(self,state):return None

class RegressionTests(unittest.TestCase):
    def test_telemetry_record(self):
        fake=FakeRsp()
        info=js.telemetry(fake,0x80004000)
        self.assertEqual(info["count"],2)
        self.assertEqual(info["phase"],"showing")
        self.assertEqual(info["kind"],10)
        self.assertIn("texture0.bti",info["name"])

    def test_reject_invalid_telemetry(self):
        fake=FakeRsp(phase=99)
        with self.assertRaises(RuntimeError):
            js.telemetry(fake,0x80004000)

    def test_captures_are_indexed_and_source_paths_preserved(self):
        session=FakeSession(3)
        with tempfile.TemporaryDirectory() as tmp:
            records=js.capture_cases(session,0x80004000,Path(tmp))
            self.assertEqual(len(records),3)
            self.assertEqual(records[2]["index"],2)
            self.assertEqual(len(session.commands),4)
            self.assertEqual(session.commands[0][1],0)
            self.assertEqual(session.commands[1][1],js.BUTTONS["A"])
            self.assertTrue((Path(tmp)/"case-02.png").exists())

    def test_golden_matches_missing_and_mismatch(self):
        session=FakeSession(2)
        with tempfile.TemporaryDirectory() as tmp:
            baseline=[{"name":"demo.iso/dvd:/assets/texture0.bti",
                       "sha256":"0"*64}]
            records=js.capture_cases(session,0x80004000,Path(tmp),baseline)
            self.assertEqual(records[0]["golden"],"match")
            self.assertEqual(records[1]["golden"],"missing")
        session=FakeSession(1)
        with tempfile.TemporaryDirectory() as tmp:
            baseline=[{"name":"demo.iso/dvd:/assets/texture0.bti",
                       "sha256":"a"*64}]
            records=js.capture_cases(session,0x80004000,Path(tmp),baseline)
            self.assertEqual(records[0]["golden"],"mismatch")

    def test_absent_corpus_rejected(self):
        session=FakeSession(0,phase=5)
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(RuntimeError):
                js.capture_cases(session,0x80004000,Path(tmp))

if __name__=="__main__":
    unittest.main()
