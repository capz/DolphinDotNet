#!/usr/bin/env python3
"""Black-box adapter/RSP tests; requires a built adapter and no native toolchain."""
import hashlib
import importlib.util
import json
from pathlib import Path
import socket
import sys
import tempfile
import threading
import time
import unittest

spec = importlib.util.spec_from_file_location("dap_probe", Path(__file__).resolve().parents[1] / "scripts/dnd_dap_probe.py")
probe = importlib.util.module_from_spec(spec)
spec.loader.exec_module(probe)
ADAPTER = Path(__file__).resolve().parents[1] / "tools/DolphinDotNet.DebugAdapter/bin/Release/net8.0/DolphinDotNet.DebugAdapter.dll"


class ProbeTests(unittest.TestCase):
    def test_only_actual_source_advancement_passes_acceptance(self):
        probe.require_source_step(dict(reason="step", description="SourceLocation"))
        for description in ["AmbiguousSource", "InstructionLimit", "UnwindUnavailable", None]:
            with self.subTest(description=description), self.assertRaises(AssertionError):
                probe.require_source_step(dict(reason="step", description=description))


class Target:
    def __init__(self):
        self.listener = socket.socket()
        self.listener.bind(("127.0.0.1", 0))
        self.listener.listen()
        self.port = self.listener.getsockname()[1]
        self.pc = 0x80004000
        self.breakpoints = set()
        self.commands = []
        self.errors = []
        self.thread = threading.Thread(target=self.run, daemon=True)
        self.thread.start()

    def reply(self, value):
        payload = value.encode()
        self.peer.sendall(b"+$" + payload + f"#{sum(payload) & 255:02x}".encode())

    def run(self):
        try:
            self.peer, _ = self.listener.accept()
            with self.peer:
                while True:
                    byte = self.peer.recv(1)
                    if not byte:
                        return
                    if byte == b"+":
                        continue
                    if byte == b"\x03":
                        self.reply(f"T0240:{self.pc:08x};")
                        continue
                    assert byte == b"$", byte
                    payload = b""
                    while True:
                        byte = self.peer.recv(1)
                        if byte == b"#":
                            break
                        assert byte
                        payload += byte
                    checksum = self.peer.recv(2)
                    assert int(checksum, 16) == sum(payload) & 255
                    command = payload.decode()
                    self.commands.append(command)
                    if command == "?":
                        self.reply(f"T0540:{self.pc:08x};")
                    elif command.startswith("Z0,"):
                        self.breakpoints.add(int(command.split(",")[1], 16))
                        self.reply("OK")
                    elif command.startswith("z0,"):
                        self.breakpoints.discard(int(command.split(",")[1], 16))
                        self.reply("OK")
                    elif command == "s":
                        self.pc += 4
                        self.reply(f"T0540:{self.pc:08x};")
                    elif command == "c":
                        candidates = sorted(b for b in self.breakpoints if b >= self.pc)
                        if candidates:
                            self.pc = candidates[0]
                            self.reply(f"T0540:{self.pc:08x};")
                        else:
                            self.peer.sendall(b"+")
                    elif command == "p1":
                        self.reply("81700000")
                    else:
                        raise AssertionError(command)
        except Exception as error:
            self.errors.append(error)
        finally:
            self.listener.close()


class AdapterTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.elf = self.root / "Game.elf"
        self.elf.write_bytes(b"fake ELF for identity tests")
        self.source = self.root / "source.json"
        self.source.write_text(json.dumps(dict(schemaVersion=2, methods=[dict(symbol="main", declaringType="Program", name="Main")])) )
        self.address = self.root / "addresses.json"
        self.address.write_text(json.dumps(dict(schemaVersion=1,
            elfSha256=hashlib.sha256(self.elf.read_bytes()).hexdigest(),
            sourceMapSha256=hashlib.sha256(self.source.read_bytes()).hexdigest(),
            methods=[dict(symbol="main", nativeStart="0x80004000", nativeSize=256, breakpoints=[
                dict(document="/build/日本/Game.cs", line=10, addresses=["0x80004004"]),
                dict(document="/build/日本/Game.cs", line=11, addresses=["0x80004008"]),
                dict(document="/build/日本/Game.cs", line=12, addresses=["0x8000400c"]),
                dict(document="/build/日本/Other.cs", line=5, addresses=["0x80004004"])])],
            unwind=[dict(start="0x80004000", end="0x80004100", supported=True,
                         cfaRegister=1, cfaOffset=0, registers={"65": dict(kind="undefined")})])))
        self.client = probe.DapProcess(ADAPTER)
        self.target = None
        self.client.request("initialize", dict(pathFormat="path", linesStartAt1=True), fragment=True)

    def tearDown(self):
        self.client.close()
        if self.target:
            self.target.thread.join(timeout=2)
            self.assertFalse(self.target.errors, self.target.errors)
        self.temp.cleanup()

    def attach(self, configured=True):
        self.target = Target()
        self.client.request("attach", dict(host="127.0.0.1", port=self.target.port,
            elf=str(self.elf), sourceMap=str(self.source), addressMap=str(self.address),
            sourceFileMap={"/build/日本": "/local/日本"}))
        self.client.event("initialized")
        if configured:
            self.client.request("configurationDone")
            self.client.event("stopped")

    def bp(self, path="Game.cs", lines=(10,)):
        return self.client.request("setBreakpoints", dict(source=dict(path="/local/日本/" + path),
            breakpoints=[dict(line=line) for line in lines]))["body"]["breakpoints"]

    def test_lifecycle_breakpoints_shared_ownership_and_stepping(self):
        self.attach()
        first = self.bp()
        self.assertTrue(first[0]["verified"])
        self.assertEqual(first[0]["id"], self.bp()[0]["id"])
        self.bp("Other.cs", (5,))
        self.bp(lines=())
        self.assertNotIn("z0,80004004,4", self.target.commands)
        self.client.request("continue", dict(threadId=1))
        stop = self.client.event("stopped")
        self.assertEqual(stop["reason"], "breakpoint")
        self.assertEqual(len(stop["hitBreakpointIds"]), 1)
        self.bp("Other.cs", ())
        self.bp(lines=(11,))
        # Two source identities share the current PC; the adapter must omit a guessed source.
        frames = self.client.request("stackTrace", dict(threadId=1))["body"]["stackFrames"]
        self.assertIsNone(frames[0]["source"])
        frame_id = frames[0]["id"]
        self.assertEqual(self.client.request("scopes", dict(frameId=frame_id))["body"]["scopes"], [])
        self.client.request("stepIn", dict(threadId=1))
        self.assertEqual(self.client.event("stopped")["description"], "AmbiguousSource")
        self.client.request("continue", dict(threadId=1))
        self.assertEqual(self.client.event("stopped")["reason"], "breakpoint")
        self.bp(lines=(12,))
        self.client.request("stepIn", dict(threadId=1))
        self.assertEqual(self.client.event("stopped")["reason"], "breakpoint")
        self.assertIn("Z0,80004008,4", self.target.commands)
        self.client.request("scopes", dict(frameId=frame_id), success=False)
        frames = self.client.request("stackTrace", dict(threadId=1))["body"]["stackFrames"]
        self.assertEqual(frames[0]["line"], 12)
        self.assertEqual(frames[0]["source"]["path"], "/local/日本/Game.cs")
        self.client.request("disconnect")
        self.client.event("terminated")
        self.assertEqual(self.client.process.wait(timeout=5), 0)
        self.assertFalse(self.target.breakpoints)

    def test_pause_and_disconnect_while_continuing(self):
        self.attach()
        self.client.request("continue", dict(threadId=1))
        self.client.request("stackTrace", dict(threadId=1), success=False)
        self.client.request("pause", dict(threadId=1))
        self.assertEqual(self.client.event("stopped")["reason"], "pause")
        # Resume after interrupt without consuming a stale stop reply.
        self.client.request("continue", dict(threadId=1))
        self.client.request("pause", dict(threadId=1))
        self.assertEqual(self.client.event("stopped")["reason"], "pause")
        self.client.request("continue", dict(threadId=1))
        self.client.request("disconnect")
        self.client.event("terminated")
        self.assertEqual(self.client.process.wait(timeout=5), 0)

    def test_stale_metadata_rejected_before_connect(self):
        self.elf.write_bytes(b"wrong ELF")
        self.client.request("attach", dict(host="127.0.0.1", port=1, elf=str(self.elf),
            sourceMap=str(self.source), addressMap=str(self.address)), success=False)

    def test_unmapped_breakpoints_and_unsupported_requests(self):
        self.attach()
        self.assertFalse(self.bp(lines=(999,))[0]["verified"])
        self.client.request("launch", success=False)
        self.client.request("evaluate", dict(expression="x"), success=False)
        self.client.request("variables", dict(variablesReference=1), success=False)
        self.client.request("threads", {})
        self.client.request("stackTrace", dict(threadId=2), success=False)

    def test_uri_paths_and_zero_based_lines(self):
        self.client.close()
        self.client = probe.DapProcess(ADAPTER)
        self.client.request("initialize", dict(pathFormat="uri", linesStartAt1=False, columnsStartAt1=False))
        self.attach()
        result = self.client.request("setBreakpoints", dict(
            source=dict(path="file:///local/%E6%97%A5%E6%9C%AC/Game.cs"), breakpoints=[dict(line=10)]))
        self.assertTrue(result["body"]["breakpoints"][0]["verified"])
        self.client.request("continue", dict(threadId=1))
        self.client.event("stopped")
        frame = self.client.request("stackTrace", dict(threadId=1))["body"]["stackFrames"][0]
        self.assertEqual(frame["line"], 10)
        self.assertEqual(frame["column"], 0)
        self.assertEqual(frame["source"]["path"], "file:///local/%E6%97%A5%E6%9C%AC/Game.cs")

    def test_duplicate_length_header_rejected(self):
        self.client.process.stdin.write(b"Content-Length: 2\r\nContent-Length: 2\r\n\r\n{}")
        self.client.process.stdin.flush()
        self.assertEqual(self.client.process.wait(timeout=5), 1)

    def test_configuration_gate(self):
        self.attach(configured=False)
        self.client.request("continue", dict(threadId=1), success=False)


if __name__ == "__main__":
    unittest.main()
