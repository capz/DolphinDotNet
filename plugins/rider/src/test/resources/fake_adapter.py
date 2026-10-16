"""Small DAP peer for testing the Java client, not a target/hardware simulator."""
import json
import sys
import os
import time

mode = sys.argv[2] if len(sys.argv) > 2 else "normal"
if mode == "earlyExit":
    sys.exit(7)
if mode == "closeStdout":
    os.close(1)
    while True:
        time.sleep(3600)
seq = 0
log = open(sys.argv[1], "w")

def send(message):
    global seq
    seq += 1
    body = json.dumps(dict(seq=seq, **message)).encode()
    sys.stdout.buffer.write(f"Content-Length: {len(body)}\r\n\r\n".encode() + body)
    sys.stdout.buffer.flush()

def event(name, **body):
    send(dict(type="event", event=name, body=body))

while True:
    header = {}
    while True:
        line = sys.stdin.buffer.readline()
        if not line:
            sys.exit(0)
        if line == b"\r\n":
            break
        key, value = line.decode().split(":", 1)
        header[key.lower()] = value.strip()
    req = json.loads(sys.stdin.buffer.read(int(header["content-length"])))
    log.write(json.dumps(req) + "\n")
    log.flush()
    cmd = req["command"]
    if mode == "unresponsive":
        while True:
            time.sleep(3600)
    if cmd == "attach" and mode == "rejectAttach":
        send(dict(type="response", request_seq=req["seq"], command=cmd, success=False, message="attach refused"))
        continue
    body = {}
    if cmd == "initialize":
        body = dict(supportsConfigurationDoneRequest=True)
    elif cmd == "stackTrace":
        body = dict(stackFrames=[dict(id=7, name="Program.Main", line=12, column=1,
                    source=dict(path="/source/Game.cs"))], totalFrames=1)
    elif cmd == "setBreakpoints":
        body = dict(breakpoints=[dict(id=i+1, verified=True, line=p["line"])
                    for i, p in enumerate(req["arguments"]["breakpoints"])])
    elif cmd == "continue":
        body = dict(allThreadsContinued=True)
    send(dict(type="response", request_seq=req["seq"], command=cmd, success=True, body=body))
    if cmd == "attach" and mode != "missingInitialized":
        event("initialized")
    elif cmd == "configurationDone":
        event("stopped", reason="entry", threadId=1, allThreadsStopped=True)
        if mode == "crashAfterConfigure":
            sys.exit(9)
    elif cmd == "continue":
        event("continued", threadId=1, allThreadsContinued=True)
    elif cmd in ("pause", "stepIn", "next", "stepOut"):
        event("stopped", reason="pause" if cmd == "pause" else "step", threadId=1, allThreadsStopped=True)
    elif cmd == "disconnect":
        assert req["arguments"]["terminateDebuggee"] is False
        event("terminated")
        break
