#!/usr/bin/env python3
"""Automated real-console asset corpus validation over DolphinDotNet's GDB RSP.

Requires a running HardwareAssetCompatibility Debug DOL, a matching ELF,
USB Gecko or BBA TCP GDB stub, and exclusive debugger ownership.
Never copies retail assets to the host; only screen captures and metadata.
"""
import argparse
import hashlib
import json
from pathlib import Path
import socket
import struct
import subprocess
import sys
import shutil

import debug_hardware_automation as debugger

PROBE_MAGIC=0x444e4143
PROBE_BYTES=256
STATUSES={1:"scanning",2:"ready",3:"pass",4:"unsupported",5:"fail",6:"empty",7:"finished"}
KINDS={0:"none",1:"bti",2:"jpa",3:"j3d"}
MAX_CASES=32


def probe_symbol(elf):
    nm=shutil.which("powerpc-eabi-nm") or shutil.which("nm")
    if not nm:
        raise RuntimeError("Missing PowerPC nm for ELF symbol lookup.")
    result=subprocess.run([nm,"-g","--defined-only",str(elf)],
                          text=True,capture_output=True,check=True)
    for line in result.stdout.splitlines():
        parts=line.split()
        if len(parts)==3 and parts[2]=="dnd_asset_compat_record":
            return int(parts[0],16)
    raise RuntimeError("Missing DOL asset probe: use the HardwareAssetCompatibility Debug ELF.")


def read_probe(rsp,address):
    data=rsp.memory(address,PROBE_BYTES)
    magic,version,serial,status,index,total,kind,visited=struct.unpack_from(">8I",data)
    if magic!=PROBE_MAGIC or version!=1:
        raise RuntimeError("Missing or incompatible DOL fixture result record.")
    if status not in STATUSES or kind not in KINDS or total>MAX_CASES or visited>2048:
        raise RuntimeError("Invalid asset fixture record.")
    if index==0xffffffff:
        index=-1
    if index<-1 or index>=total:
        raise RuntimeError("Invalid asset fixture index.")
    name=data[32:256].split(b"\x00",1)[0].decode("ascii","replace")
    return {"serial":serial,"status":STATUSES[status],"index":index,
            "total":total,"kind":KINDS[kind],"scannedEntries":visited,
            "asset":name}


def one_poll(session,buttons):
    session.command(0,buttons=buttons,polls=1,connected=True)
    state=session.frame()
    session.verify_ack(state)


def run(session,probe_address,output,baseline=None,allow_unsupported=True):
    session.begin()
    initial=read_probe(session.rsp,probe_address)
    if initial["status"]!="ready" or initial["index"]!=-1 or not 1<=initial["total"]<=MAX_CASES:
        raise RuntimeError("No discoverable compatible assets or test DOL failed to mount: "+str(initial))
    references={}
    if baseline is not None:
        references=json.loads(Path(baseline).read_text(encoding="utf-8"))
        if not isinstance(references,dict):
            raise ValueError("Golden baseline must be a JSON object keyed by asset path.")
    output=Path(output)
    output.mkdir(parents=True,exist_ok=True)
    records=[]
    failures=[]
    for number in range(initial["total"]):
        # Neutral sample establishes connection and a released baseline.
        one_poll(session,0)
        one_poll(session,debugger.BUTTONS["A"])
        record=read_probe(session.rsp,probe_address)
        if record["index"]!=number or record["status"] not in ("pass","unsupported","fail"):
            raise RuntimeError(f"Fixture did not select case {number}: {record}")
        image=session.capture(output/f"asset-{number:03d}.png")
        outcome={**record,**image}
        name=record["asset"]
        if name in (x["asset"] for x in records):
            raise RuntimeError("Duplicate or truncated asset record identity: "+name)
        if record["status"]=="fail" or (record["status"]=="unsupported" and not allow_unsupported):
            failures.append(f"{name}: {record['status']}")
        if baseline is not None:
            expected=references.get(name)
            if expected is None:
                failures.append(name+": missing baseline")
            elif not isinstance(expected,str) or expected.lower()!=image["sha256"]:
                failures.append(name+": raw XFB hash mismatch")
        records.append(outcome)

    one_poll(session,0)
    one_poll(session,debugger.BUTTONS["A"])
    complete=read_probe(session.rsp,probe_address)
    if complete["status"]!="finished" or complete["index"]!=initial["total"]-1:
        raise RuntimeError("Fixture failed to reach terminal state: "+str(complete))
    document={"protocol":1,"discovered":len(records),"scannedEntries":initial["scannedEntries"],
              "passed":sum(r["status"]=="pass" for r in records),
              "unsupported":sum(r["status"]=="unsupported" for r in records),
              "failed":len(failures),"failures":failures,"captures":records}
    (output/"compatibility.json").write_text(json.dumps(document,indent=2)+"\n",encoding="utf-8")
    (output/"baseline-candidate.json").write_text(
        json.dumps({r["asset"]:r["sha256"] for r in records if r["status"]=="pass"},indent=2)+"\n",
        encoding="utf-8")
    return document


def main(argv=None):
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--elf",type=Path,required=True)
    conn=parser.add_mutually_exclusive_group(required=True)
    conn.add_argument("--tcp",help="GDB RSP host:port")
    conn.add_argument("--serial",help="USB Gecko COM port or device file")
    parser.add_argument("--baud",type=int,default=115200)
    parser.add_argument("--timeout",type=float,default=180)
    parser.add_argument("--output",type=Path,required=True)
    parser.add_argument("--baseline",type=Path,help="Previously approved raw XFB hashes")
    parser.add_argument("--require-all",action="store_true",help="Fail on unsupported formats too")
    args=parser.parse_args(argv)
    if not 1<=args.timeout<=600 or not 1<=args.baud<=3000000:
        parser.error("Invalid timeout or baud rate.")
    mailbox,boundary=debugger.symbols(args.elf)
    asset_probe=probe_symbol(args.elf)
    sock=None
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
        except ImportError as error:
            raise RuntimeError("For USB Gecko run: pip install pyserial") from error
        stream=serial.Serial(args.serial,baudrate=args.baud,timeout=args.timeout,
                             write_timeout=args.timeout)
    try:
        session=debugger.HardwareSession(debugger.Rsp(stream),mailbox,boundary)
        result=run(session,asset_probe,args.output,args.baseline,not args.require_all)
        print(json.dumps({k:v for k,v in result.items() if k!="captures"},indent=2))
        if result["failed"]:
            raise RuntimeError(f"{result['failed']} compatibility regressions; see compatibility.json")
    finally:
        stream.close()
        if sock is not None:sock.close()


if __name__=="__main__":
    try:main()
    except (OSError,ValueError,RuntimeError,TimeoutError,AssertionError) as error:
        print("Hardware asset compatibility run failed:",str(error),file=sys.stderr)
        sys.exit(1)
