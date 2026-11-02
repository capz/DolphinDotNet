"""Offline corpus-result and native-frame regression tests; no owned assets required."""
import io
import json
import struct
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0,str(Path(__file__).resolve().parents[1]/"scripts"))
import hardware_asset_corpus as corpus
import debug_hardware_automation as auto


class SimulatedRSP:
    def __init__(self):
        self.mailbox=0x80001200
        self.corpus=0x80001300
        self.ram=bytearray(0x60)
        self.ram[:8]=auto.pack(auto.MAGIC,auto.VERSION)
        self.report=bytearray(corpus.SIZE)
        self.report[:8]=auto.pack(corpus.MAGIC,corpus.VERSION)
        self.frame=0
        self.hook=0x80002000
        self.img=bytes([16,128,235,128,235,128,16,128]*2)
        self.steps=[]
    def get_word(self, data, offset):return struct.unpack_from(">I",data,offset)[0]
    def put_word(self, data, offset, value):struct.pack_into(">I",data,offset,value)
    def memory(self, address, count):
        if self.mailbox<=address<self.mailbox+len(self.ram):
            offset=address-self.mailbox
            return bytes(self.ram[offset:offset+count])
        if address==self.corpus:return bytes(self.report[:count])
        if address==0xc0120000:return self.img
        raise IOError("Unknown test memory address.")
    def write(self,address,data):
        offset=address-self.mailbox
        if offset<0 or offset+len(data)>len(self.ram):raise IOError("Out of bounds")
        self.ram[offset:offset+len(data)]=data
    def breakpoint(self,address,enable=True):
        if address!=self.hook:raise IOError("Unexpected frame breakpoint.")
    def stop(self,operation):
        self.steps.append(operation)
        if operation=="c":
            self.frame+=1
            self.put_word(self.ram,0x44,self.frame)
            self.put_word(self.ram,0x40,self.frame)
            for offset,value in ((0x48,0xc0120000),(0x4c,4),(0x50,2),
                                 (0x54,8),(0x58,16),(0x5c,1)):
                self.put_word(self.ram,offset,value)
            request=self.get_word(self.ram,8)
            if request!=self.get_word(self.ram,12):
                self.put_word(self.ram,12,request)
                self.put_word(self.ram,16,1)
            if self.frame==2:
                self.put_word(self.report,8,1)
                self.put_word(self.report,12,1)
                self.put_word(self.report,16,1)
                self.put_word(self.report,20,2048)
                self.put_word(self.report,24,32768)
                name=b"GM8E01-0-0#dvd:/texture.bti"
                self.report[64:64+len(name)]=name
            if self.frame==3:
                self.put_word(self.report,28,1)
                self.put_word(self.report,32,46)
                self.put_word(self.report,36,1)
                self.put_word(self.report,40,1)
                self.put_word(self.report,44,1)
        return "S05"


class CorpusTests(unittest.TestCase):
    def test_report_shape_and_magic(self):
        data=bytearray(corpus.SIZE)
        struct.pack_into(">2I",data,0,corpus.MAGIC,corpus.VERSION)
        data[64:68]=b"test"
        self.assertEqual(corpus.parse_report(data)["path"],"test")
        for bad in (b"",data[:-1],b"INVALID"+data[7:]):
            with self.assertRaises((ValueError,RuntimeError)):
                corpus.parse_report(bad)

    def test_capture_and_baseline(self):
        fake=SimulatedRSP()
        session=auto.HardwareSession(fake,fake.mailbox,fake.hook)
        session.begin()
        with tempfile.TemporaryDirectory() as td:
            output=Path(td)
            result=corpus.collect(session,fake.corpus,output,max_frames=5)
            self.assertEqual(result["discsMounted"],1)
            self.assertTrue(result["syntheticInputAcknowledged"])
            self.assertEqual(result["records"][0]["kind"],"bti")
            self.assertEqual(result["records"][0]["status"],"ok")
            self.assertEqual(result["records"][0]["width"],4)
            self.assertTrue((output/"asset-000.png").is_file())
            self.assertIsNone(corpus.compare(result,result))
            modified=json.loads(json.dumps(result))
            modified["records"][0]["rawXfbSha256"]="0"*64
            with self.assertRaises(AssertionError):
                corpus.compare(result,modified)
            changed=json.loads(json.dumps(result))
            changed["records"][0]["gxBytes"]=65536
            with self.assertRaises(AssertionError):
                corpus.compare(result,changed)

    def test_missing_assets_are_not_success(self):
        fake=SimulatedRSP()
        fake.frame=2  # next test stop reports completion
        session=auto.HardwareSession(fake,fake.mailbox,fake.hook)
        session.begin()
        self.assertEqual(corpus.parse_report(fake.report)["serial"],0)
        with tempfile.TemporaryDirectory() as td:
            with self.assertRaises(RuntimeError):
                corpus.collect(session,fake.corpus,td,max_frames=1)

    def test_reference_classification_changes_fail(self):
        expected={"records":[{"kind":"jpa","path":"GDME01-0-0#dvd:/effect.jpc",
                              "status":"unsupported","visual":False,
                              "gxBytes":0}]}
        actual=json.loads(json.dumps(expected))
        actual["records"][0]["status"]="ok"
        with self.assertRaises(AssertionError):corpus.compare(expected,actual)


if __name__=="__main__":
    unittest.main()
