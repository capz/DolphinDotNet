#!/usr/bin/env python3
"""Black-box DAP acceptance client. The smoke harness owns Dolphin, not this process."""
import argparse
import json
from pathlib import Path
import queue
import subprocess
import threading
import time


class DapProcess:
    def __init__(self, adapter):
        self.process = subprocess.Popen(["dotnet", str(adapter)], stdin=subprocess.PIPE,
                                        stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        self.messages = queue.Queue()
        self.events = []
        self.sequence = 0
        threading.Thread(target=self._read, daemon=True).start()

    def _read(self):
        try:
            while True:
                header = {}
                while True:
                    line = self.process.stdout.readline()
                    if not line:
                        raise EOFError("Adapter closed stdout")
                    if line == b"\r\n":
                        break
                    key, value = line.decode("ascii").split(":", 1)
                    header[key.lower()] = value.strip()
                size = int(header["content-length"])
                body = self.process.stdout.read(size)
                if len(body) != size:
                    raise EOFError("Truncated adapter message")
                self.messages.put(json.loads(body))
        except Exception as error:
            self.messages.put(error)

    def send(self, command, arguments=None, fragment=False):
        self.sequence += 1
        payload = json.dumps(dict(seq=self.sequence, type="request", command=command,
                                  arguments=arguments or {}), ensure_ascii=False).encode()
        frame = f"Content-Length: {len(payload)}\r\n\r\n".encode() + payload
        if fragment:
            for value in frame:
                self.process.stdin.write(bytes([value]))
                self.process.stdin.flush()
        else:
            self.process.stdin.write(frame)
            self.process.stdin.flush()
        return self.sequence

    def _next(self, timeout=15):
        message = self.messages.get(timeout=timeout)
        if isinstance(message, Exception):
            raise message
        return message

    def response(self, sequence, success=True):
        while True:
            message = self._next()
            if message["type"] == "event":
                self.events.append(message)
                continue
            assert message["request_seq"] == sequence, message
            assert message["success"] == success, message
            return message

    def request(self, command, arguments=None, success=True, fragment=False):
        return self.response(self.send(command, arguments, fragment), success)

    def event(self, name):
        for index, message in enumerate(self.events):
            if message["event"] == name:
                return self.events.pop(index)["body"]
        while True:
            message = self._next()
            assert message["type"] == "event", message
            if message["event"] == name:
                return message["body"]
            self.events.append(message)

    def close(self):
        if not self.process.stdin.closed:
            self.process.stdin.close()
        if self.process.poll() is None:
            try:
                self.process.wait(timeout=6)
            except subprocess.TimeoutExpired:
                self.process.kill()
                self.process.wait()
        self.process.stdout.close()
        self.process.stderr.close()


def run_probe(args):
    client = DapProcess(args.adapter)
    try:
        client.request("initialize", dict(adapterID="dolphindotnet", linesStartAt1=True,
                                          columnsStartAt1=True, pathFormat="path"))
        attach = dict(host="127.0.0.1", port=args.port, elf=str(args.elf),
                      sourceMap=str(args.source_map), addressMap=str(args.address_map))
        # Dolphin's GDB listener appears after boot. Retry attach without reusing an RSP connection.
        deadline = time.monotonic() + 15
        while True:
            seq = client.send("attach", attach)
            while True:
                response = client._next()
                if response["type"] == "response":
                    assert response["request_seq"] == seq, response
                    break
                client.events.append(response)
            if response["success"]:
                break
            if time.monotonic() >= deadline:
                raise RuntimeError(response)
            time.sleep(.15)
        client.event("initialized")
        client.request("configurationDone")
        client.event("stopped")
        specifications = [s.rsplit(":", 1) for s in args.sources]
        if args.stack:
            specifications = specifications[:1]
        for path, line in specifications:
            response = client.request("setBreakpoints", dict(source=dict(path=path), breakpoints=[dict(line=int(line))]))
            assert all(b["verified"] for b in response["body"]["breakpoints"]), response
            client.request("continue", dict(threadId=1))
            stop = client.event("stopped")
            assert stop["reason"] == "breakpoint", stop
            frames = client.request("stackTrace", dict(threadId=1))["body"]["stackFrames"]
            assert frames and frames[0]["line"] == int(line), frames
            print(f"DND_DAP_SOURCE_BREAKPOINT {path}:{line}", flush=True)
            client.request("setBreakpoints", dict(source=dict(path=path), breakpoints=[]))
        if args.stack:
            names = [frame["name"] for frame in frames]
            assert names[:3] == ["Program.Inner", "Program.Outer", "Program.Main"], names
            print("DND_DAP_MANAGED_STACK " + " -> ".join(names[:3]), flush=True)
            client.request("stepOut", dict(threadId=1))
            assert client.event("stopped")["reason"] == "step"
            frames = client.request("stackTrace", dict(threadId=1))["body"]["stackFrames"]
            assert frames[0]["name"] == "Program.Outer" and frames[0]["line"] == 14, frames
            print("DND_DAP_STEP_OUT Program.Outer:14", flush=True)
            client.request("next", dict(threadId=1))
            assert client.event("stopped")["reason"] == "step"
            frames = client.request("stackTrace", dict(threadId=1))["body"]["stackFrames"]
            assert frames[0]["name"] == "Program.Outer" and frames[0]["line"] == 15, frames
            print("DND_DAP_STEP_OVER Program.Outer:15", flush=True)
        else:
            client.request("stepIn", dict(threadId=1))
            assert client.event("stopped")["reason"] == "step"
            frames = client.request("stackTrace", dict(threadId=1))["body"]["stackFrames"]
            assert frames and frames[0]["source"] and frames[0]["line"] > 0, frames
            print("DND_DAP_SOURCE_STEP", flush=True)
        client.request("continue", dict(threadId=1))
        time.sleep(.2)
        client.request("pause", dict(threadId=1))
        assert client.event("stopped")["reason"] == "pause"
        print("DND_DAP_PAUSE", flush=True)
        client.request("disconnect", dict(terminateDebuggee=False))
        client.event("terminated")
        assert client.process.wait(timeout=5) == 0
    finally:
        client.close()


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--adapter", type=Path, required=True)
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--elf", type=Path, required=True)
    parser.add_argument("--source-map", type=Path, required=True)
    parser.add_argument("--address-map", type=Path, required=True)
    parser.add_argument("--stack", action="store_true")
    parser.add_argument("sources", nargs="+")
    run_probe(parser.parse_args())
