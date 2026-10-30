"""USB Gecko compatibility runner regression tests: no console or proprietary assets."""
import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path

SCRIPTS=Path(__file__).resolve().parents[1]/"scripts"
sys.path.insert(0,str(SCRIPTS))
import run_hardware_asset_compatibility as compat

class FakeRsp:
    def __init__(self):
        self.messages=[(2,-1,0),(3,0,1),(4,1,2)]
        self.latest=(2,-1,0)
        self.frame=0
        self.pending=0
    def memory(self,address,count):
        if address!=0x80000100 or count!=256:raise AssertionError("out of scope read")
        status,index,total=self.latest
        import struct
        name=b"fixture/image.iso::dvd:/fx.bti" if index==0 else b"fixture/image.iso::dvd:/particles.jpc"
        return struct.pack(">8I",compat.PROBE_MAGIC,1,self.frame,status,
                           index&0xffffffff,total,1 if index==0 else 2,12)+name.ljust(224,b"\0")

class FakeSession:
    def __init__(self):
        self.rsp=FakeRsp()
        self.commands=[]
        self.current=-1
        self.pressed=False
        self.captures=[]
    def begin(self):return None
    def command(self,port,*,buttons,polls,connected):
        self.commands.append((port,buttons,polls,connected))
        self.pressed=buttons!=0
    def frame(self):
        self.rsp.frame+=1
        if self.pressed:
            self.current+=1
            if self.current==0:self.rsp.latest=(3,0,2)
            elif self.current==1:self.rsp.latest=(4,1,2)
            else:self.rsp.latest=(7,1,2)
        return [0,0,0,1,1]
    def verify_ack(self,state):pass
    def capture(self,path):
        p=Path(path)
        p.write_bytes(b"\x89PNG\r\n\x1a\nfixture")
        self.captures.append(p)
        return {"frame":self.rsp.frame,"width":4,"height":4,"sha256":str(self.current)*64,
                "rawBytes":32,"png":str(p)}

class Tests(unittest.TestCase):
    def test_probe_validation_and_identity(self):
        r=FakeRsp()
        s=compat.read_probe(r,0x80000100)
        self.assertEqual(s["status"],"ready")
        self.assertEqual(s["index"],-1)
        r.latest=(5,0,1)
        self.assertEqual(compat.read_probe(r,0x80000100)["status"],"fail")

    def test_runs_all_cases_and_writes_reviewable_baseline(self):
        runner=FakeSession()
        runner.rsp.latest=(2,-1,2)
        with tempfile.TemporaryDirectory() as folder:
            output=Path(folder)
            result=compat.run(runner,0x80000100,output)
            self.assertEqual(result["discovered"],2)
            self.assertEqual(result["passed"],1)
            self.assertEqual(result["unsupported"],1)
            self.assertEqual(result["failed"],0)
            self.assertEqual(len(runner.captures),2)
            self.assertEqual([x[1] for x in runner.commands],[0,256,0,256,0,256])
            self.assertTrue((output/"compatibility.json").exists())
            self.assertTrue((output/"baseline-candidate.json").exists())
            import json
            baseline=json.loads((output/"baseline-candidate.json").read_text())
            self.assertEqual(len(baseline),1)
    def test_baseline_mismatch_fails_and_is_reported(self):
        runner=FakeSession()
        runner.rsp.latest=(2,-1,2)
        with tempfile.TemporaryDirectory() as folder:
            output=Path(folder)
            import json
            baseline=output/"golden.json"
            baseline.write_text(json.dumps({"fixture/image.iso::dvd:/fx.bti":"f"*64,
                "fixture/image.iso::dvd:/particles.jpc":"1"*64}))
            result=compat.run(runner,0x80000100,output,baseline)
            self.assertEqual(result["failed"],1)
            self.assertTrue(any("mismatch" in x for x in result["failures"]))
    def test_rejects_empty_corpus(self):
        runner=FakeSession()
        runner.rsp.latest=(6,-1,0)
        with tempfile.TemporaryDirectory() as folder:
            with self.assertRaises(RuntimeError):
                compat.run(runner,0x80000100,folder)

if __name__=="__main__":unittest.main()
