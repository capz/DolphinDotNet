#!/usr/bin/env python3
"""Read-only JSystem retail-asset corpus runner for a physical GameCube or Dolphin.

Boot tests/RetailAssetCorpus as a Debug ELF with the existing GDB stub first.
This host tool exclusively owns USB Gecko/TCP RSP; no game data are exported.
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
from pathlib import Path

from debug_hardware_automation import HardwareSession, Rsp, symbols as frame_symbols

MAGIC=0x444e4346  # DNCF
VERSION=1
SIZE=224
KIND={1:"bti",2:"j3d",3:"jpa",4:"bmg",5:"bfn",6:"rarc"}
STATUS={1:"ok",2:"unsupported",3:"malformed",4:"runtime-error"}
VISUAL={1,2,3}
MAX_RECORDS=40

def corpus_symbol(elf):
    nm=shutil.which("powerpc-eabi-nm") or shutil.which("nm")
    if not nm:raise RuntimeError("powerpc-eabi-nm or nm is required.")
    output=subprocess.run([nm,"-g","--defined-only",str(elf)],
                          capture_output=True,text=True,check=True).stdout
    candidates=[]
    for line in output.splitlines():
        match=re.fullmatch(r"\s*([0-9a-fA-F]{8,16})\s+[A-Za-z]\s+dnd_asset_corpus_report\s*",line)
        if match:candidates.append(int(match[1],16))
    if len(candidates)!=1:
        raise RuntimeError("Expected dnd_asset_corpus_report in Debug ELF; "
                           "build RetailAssetCorpus with DndDebugSymbols=true.")
    return candidates[0]

def parse_report(data):
    if len(data)!=SIZE:raise ValueError("Truncated corpus report.")
    words=struct.unpack(">16I",data[:64])
    if words[:2]!=(MAGIC,VERSION):raise RuntimeError("Invalid corpus ABI.")
    path=data[64:].split(b"\0",1)[0].decode("ascii")
    return {
        "serial":words[2], "kind":words[3], "status":words[4],
        "assetBytes":words[5], "gxBytes":words[6],
        "discsMounted":words[7], "entriesScanned":words[8],
        "candidates":words[9], "completed":words[10],
        "finished":bool(words[11]), "path":path
    }

def record_key(record):
    return record["path"]+"|"+record["kind"]

def collect(session,report_address,output,max_frames=120,require_assets=True):
    """Drives the existing frame breakpoint and input injection APIs.

    Returns a JSON-compatible report. The debugger remains halted afterward.
    """
    if max_frames<1 or max_frames>7200:raise ValueError("max_frames must be 1..7200.")
    output=Path(output)
    output.mkdir(parents=True,exist_ok=True)
    records=[]
    seen=set()
    last_serial=0
    input_ack=False
    # Port 4 is reserved as the test-control device; synthetic A confirms
    # guest PAD polling without modifying the other three real ports.
    session.command(3,buttons=0x100,polls=3600,connected=True)
    for _ in range(max_frames):
        session.frame()
        report=parse_report(session.rsp.memory(report_address,SIZE))
        if report["serial"]!=last_serial:
            if report["serial"]<last_serial or report["serial"]-last_serial!=1:
                raise RuntimeError("Lost asset record between debugger frame stops.")
            last_serial=report["serial"]
            kind=report["kind"]
            if kind not in KIND or report["status"] not in STATUS:
                raise RuntimeError("Unknown test case kind or outcome.")
            key=record_key({"path":report["path"],"kind":KIND[kind]})
            if not report["path"] or key in seen or len(records)>=MAX_RECORDS:
                raise RuntimeError("Missing, duplicate or oversized corpus case.")
            seen.add(key)
            index=len(records)
            target=output/f"asset-{index:03d}.png"
            capture=session.capture(target)
            records.append({
                "index":index,"path":report["path"],"kind":KIND[kind],
                "status":STATUS[report["status"]],
                "visual":kind in VISUAL and report["status"]==1,
                "assetBytes":report["assetBytes"],
                "gxBytes":report["gxBytes"],
                "frame":capture["frame"],
                "width":capture["width"],"height":capture["height"],
                "rawXfbSha256":capture["sha256"],"capture":target.name
            })
        state=session.state()
        if state[3]==session.sequence and state[4]==1:
            input_ack=True
        if report["finished"]:
            if report["serial"]!=report["completed"]:
                raise RuntimeError("Guest finished before all reported cases were captured.")
            if not input_ack:
                raise RuntimeError("Synthetic controller injection was never acknowledged.")
            if require_assets and not records:
                raise RuntimeError("No supported asset candidates found. Check GC Loader/SD mount.")
            return {
                "schema":1, "hardwareFrameSource":"gx-xfb-yuyv",
                "syntheticInputAcknowledged":True,
                "discsMounted":report["discsMounted"],
                "entriesScanned":report["entriesScanned"],
                "candidates":report["candidates"],
                "completed":report["completed"],"records":records
            }
    raise TimeoutError(f"Fixture did not complete within {max_frames} presented frames.")

def compare(reference,actual,strict=True):
    """Exact raw-XFB goldens. Decode-only cases compare classification, not pixels."""
    base={record_key(r):r for r in reference["records"]}
    got={record_key(r):r for r in actual["records"]}
    if len(base)!=len(reference["records"]) or len(got)!=len(actual["records"]):
        raise AssertionError("Duplicate asset identifiers in baseline or actual report.")
    if set(base)!=set(got):
        raise AssertionError("Corpus changed: absent="+str(sorted(set(base)-set(got)))+
                             " added="+str(sorted(set(got)-set(base))))
    failures=[]
    for key,ref in base.items():
        now=got[key]
        if ref["status"]!=now["status"]:
            failures.append(key+" status "+ref["status"]+" -> "+now["status"])
        if ref.get("visual") and now.get("visual") and \
           ref.get("rawXfbSha256")!=now.get("rawXfbSha256"):
            failures.append(key+" rendered frame differs")
        if strict and now["gxBytes"] > ref["gxBytes"]:
            failures.append(key+" native GX memory increased from "+
                            str(ref["gxBytes"])+" to "+str(now["gxBytes"]))
    if failures:
        raise AssertionError("Fixture regression:\n"+"\n".join(failures))

def main(argv=None):
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--elf",type=Path,required=True)
    target=parser.add_mutually_exclusive_group(required=True)
    target.add_argument("--tcp",help="GDB RSP host:port")
    target.add_argument("--serial",help="USB Gecko COM device")
    parser.add_argument("--baud",type=int,default=115200)
    parser.add_argument("--timeout",type=float,default=120)
    parser.add_argument("--max-frames",type=int,default=120)
    parser.add_argument("--output",type=Path,required=True)
    parser.add_argument("--baseline",type=Path,
                        help="Report JSON from an equivalent Dolphin/reference run")
    parser.add_argument("--allow-empty",action="store_true",
                        help="Smoke only; no asset discovery is not a compatibility pass")
    args=parser.parse_args(argv)
    if not 1<=args.timeout<=600 or not 1<=args.baud<=3000000:
        parser.error("Invalid timeout or baud.")
    mailbox,boundary=frame_symbols(args.elf)
    corpus=corpus_symbol(args.elf)
    if args.tcp:
        host,colon,port=args.tcp.rpartition(":")
        if not colon or not host or not port.isdecimal() or not 1<=int(port)<=65535:
            parser.error("--tcp requires host:port.")
        sock=socket.create_connection((host,int(port)),timeout=args.timeout)
        sock.settimeout(args.timeout)
        stream=sock.makefile("rwb",buffering=0)
    else:
        try:import serial
        except ImportError as error:
            raise RuntimeError("USB Gecko requires pip install pyserial.") from error
        stream=serial.Serial(args.serial,baudrate=args.baud,timeout=args.timeout,
                             write_timeout=args.timeout)
    try:
        session=HardwareSession(Rsp(stream),mailbox,boundary)
        session.begin()
        actual=collect(session,corpus,args.output,args.max_frames,
                       require_assets=not args.allow_empty)
        target=args.output/"report.json"
        target.write_text(json.dumps(actual,indent=2,sort_keys=True)+"\n",encoding="utf-8")
        if args.baseline:
            compare(json.loads(args.baseline.read_text(encoding="utf-8")),actual)
        print(json.dumps({"status":"pass","report":str(target),
                          "cases":len(actual["records"]),
                          "rendered":sum(1 for x in actual["records"] if x["visual"]),
                          "decodeOnly":sum(1 for x in actual["records"] if not x["visual"]),
                          "discs":actual["discsMounted"]},indent=2))
    finally:
        stream.close()
        if args.tcp:sock.close()

if __name__=="__main__":
    try:main()
    except (IOError,ValueError,RuntimeError,TimeoutError,AssertionError,
            subprocess.CalledProcessError) as error:
        print("Retail asset corpus failed: "+str(error),file=sys.stderr)
        sys.exit(1)
