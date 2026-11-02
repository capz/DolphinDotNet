#!/usr/bin/env python3
"""Capture user-owned GameCube JSystem fixture DOL scenes via USB Gecko/GDB.

Uses the standard debugger RSP/frame-boundary API from PR #141.
The source ISO/GCM files never leave the console; results contain only the
fixture's pathname, format/count telemetry and captured presented-XFB PNGs.
"""
import argparse
import json
import struct
import sys
from pathlib import Path

from debug_hardware_automation import Rsp, HardwareSession, symbols, save_png, yuyv_to_rgb
from debug_hardware_automation import BUTTONS

FIXTURE_MAGIC = 0x444e4649
PHASE = {1:"scanning",2:"ready",3:"showing",4:"done",5:"unavailable"}
MAX_CASES = 24

def fixture_address(elf):
    import re
    import shutil
    import subprocess
    nm = shutil.which("powerpc-eabi-nm") or shutil.which("nm")
    if nm is None:
        raise RuntimeError("powerpc-eabi-nm (or nm) is required.")
    result=subprocess.run([nm,"-g","--defined-only",str(elf)],capture_output=True,
                          text=True,check=True)
    for line in result.stdout.splitlines():
        match=re.fullmatch(r"\s*([0-9A-Fa-f]{8,16})\s+[A-Za-z]\s+dnd_jsystem_fixture\s*",line)
        if match:
            return int(match.group(1),16)
    raise RuntimeError("ELF lacks dnd_jsystem_fixture. Build the JSystemHardwareFixture Debug DOL.")

def telemetry(rsp,address):
    data=rsp.memory(address,248)
    parts=struct.unpack(">14I",data[:56])
    (magic,version,serial,phase,source_index,source_count,asset_index,asset_count,
     passed,skipped,failed,kind,detail,name_size)=parts
    if magic!=FIXTURE_MAGIC or version!=1 or name_size>191 or phase not in PHASE:
        raise RuntimeError("Invalid fixture DOL telemetry or incompatible version.")
    name=data[56:56+name_size].decode("ascii","replace")
    return dict(serial=serial,phase=PHASE[phase],sourceIndex=source_index,
                sourceCount=source_count,index=asset_index,count=asset_count,
                passed=passed,skipped=skipped,failed=failed,kind=kind,
                detail=detail,name=name)

def press(session,fixture_addr,buttons,frames=1):
    # One native PAD poll per frame is required by the fixture contract.
    session.command(0,buttons=buttons,polls=frames,connected=True)
    result=session.frames(frames)
    session.verify_ack(result)
    return telemetry(session.rsp,fixture_addr)

def capture_cases(session,fixture_addr,output,golden=None):
    status=telemetry(session.rsp,fixture_addr)
    if status["phase"]=="unavailable":
        raise RuntimeError("No supported image/fixture source mounted at gcloader:/games or sd:/games.")
    if status["phase"]!="showing":
        raise RuntimeError("DOL did not reach the first fixture scene: "+repr(status))
    total=status["count"]
    if total<1 or total>MAX_CASES:
        raise RuntimeError("Unexpected candidate count: "+str(total))
    output.mkdir(parents=True,exist_ok=True)
    records=[]
    expected={entry["name"]:entry["sha256"] for entry in golden} if golden is not None else None
    for case in range(total):
        if status["phase"]!="showing" or status["index"]!=case:
            raise RuntimeError("Test DOL did not advance to expected fixture: "+repr(status))
        if case:
            # Ensure no prior press bleeds into the next case; the game's
            # baseline/reconnect suppression is intentionally respected.
            pass
        key=status["name"]
        path=output/("case-%02d.png"%case)
        image=session.capture(path)
        record={**status,**image}
        if expected is not None:
            wanted=expected.get(key)
            if wanted is None:
                record["golden"]="missing"
            elif wanted.lower()!=image["sha256"].lower():
                record["golden"]="mismatch"
            else:
                record["golden"]="match"
        records.append(record)
        if case+1<total:
            # Baseline connected, release all buttons, then a fresh A press:
            # the native controller deliberately suppresses presses on connect.
            press(session,fixture_addr,0)
            status=press(session,fixture_addr,BUTTONS["A"])
    return records

def execute(elf,script_output,stream,golden=None):
    mailbox,boundary=symbols(elf)
    addr=fixture_address(elf)
    session=HardwareSession(Rsp(stream),mailbox,boundary)
    session.begin()
    # Capture begins at the first renderer-presented scene after scanning.
    records=capture_cases(session,addr,script_output,golden)
    report={"schema":1,"cases":records,
            "goldenPass":sum(1 for r in records if r.get("golden")=="match"),
            "goldenMissing":sum(1 for r in records if r.get("golden")=="missing"),
            "goldenMismatch":sum(1 for r in records if r.get("golden")=="mismatch"),
            "parseFailures":records[-1]["failed"],
            "unsupported":records[-1]["skipped"]}
    (script_output/"results.json").write_text(json.dumps(report,indent=2)+"\n",encoding="utf-8")
    if golden is not None and (report["goldenMissing"] or report["goldenMismatch"]):
        raise AssertionError("Golden comparison failed: %s missing, %s mismatched"%
                             (report["goldenMissing"],report["goldenMismatch"]))
    if report["parseFailures"]:
        raise AssertionError("Detected %d malformed or non-decodable selected assets."%report["parseFailures"])
    return report

def main(argv=None):
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--elf",required=True,type=Path)
    group=parser.add_mutually_exclusive_group(required=True)
    group.add_argument("--tcp",help="Existing GDB endpoint host:port")
    group.add_argument("--serial",help="USB Gecko serial COM/path; pyserial required")
    parser.add_argument("--baud",default=115200,type=int)
    parser.add_argument("--timeout",default=180.0,type=float)
    parser.add_argument("--output",required=True,type=Path)
    parser.add_argument("--golden",type=Path,help="Prior results.json; compare exact raw-XFB SHA-256")
    args=parser.parse_args(argv)
    if args.timeout<1 or args.timeout>1800 or args.baud<1:
        parser.error("Invalid debugger timeout/baud.")
    golden=None
    if args.golden:
        baseline=json.loads(args.golden.read_text(encoding="utf-8"))
        golden=baseline["cases"]
        if not isinstance(golden,list):
            parser.error("Golden file must contain a cases array.")
    if args.tcp:
        import socket
        host,colon,port=args.tcp.rpartition(":")
        if not colon or not host or not port.isdecimal() or not 1<=int(port)<=65535:
            parser.error("--tcp must be host:port.")
        sock=socket.create_connection((host,int(port)),timeout=args.timeout)
        sock.settimeout(args.timeout)
        stream=sock.makefile("rwb",buffering=0)
    else:
        try:import serial
        except ImportError as error:raise RuntimeError("Install pyserial for USB Gecko.") from error
        stream=serial.Serial(args.serial,baudrate=args.baud,timeout=args.timeout,
                             write_timeout=args.timeout)
    try:
        report=execute(args.elf,args.output,stream,golden)
        print(json.dumps({"result":"passed","cases":len(report["cases"]),
                          "unsupported":report["unsupported"],
                          "goldenMatches":report["goldenPass"]},indent=2))
    finally:
        stream.close()
        if args.tcp:sock.close()

if __name__=="__main__":
    try:main()
    except (AssertionError,RuntimeError,IOError,TimeoutError,ValueError,KeyError) as error:
        print("JSystem hardware fixture run failed: "+str(error),file=sys.stderr)
        sys.exit(1)
