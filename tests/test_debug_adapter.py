#!/usr/bin/env python3
"""Black-box adapter/RSP tests; requires a built adapter and no native toolchain."""
import hashlib
import importlib.util
import json
import os
import select
import subprocess
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
        self.memory = {0x81700004: bytes.fromhex("00000003"), 0x81700008: bytes.fromhex("ffffffff"),
                       0x81700010: bytes.fromhex("400c000000000000"), 0x81700018: bytes.fromhex("0000d83d")}
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
                    elif command == "p3":
                        self.reply("0000002a")
                    elif command.startswith("m"):
                        address, size = (int(v, 16) for v in command[1:].split(","))
                        self.reply(self.memory.get(address, bytes(size))[:size].hex())
                    else:
                        raise AssertionError(command)
        except Exception as error:
            self.errors.append(error)
        finally:
            self.listener.close()


class SerialRelay:
    """PTY byte transport to the fake RSP target. No physical USB/EXI emulation."""
    def __init__(self, port):
        import tty
        self.master, self.slave = os.openpty()
        tty.setraw(self.slave)
        self.path = os.ttyname(self.slave)
        self.peer = socket.create_connection(("127.0.0.1", port))
        self.stopping = threading.Event()
        self.thread = threading.Thread(target=self.run, daemon=True)
        self.thread.start()

    def run(self):
        try:
            while not self.stopping.is_set():
                ready, _, _ = select.select([self.master, self.peer], [], [], 0.1)
                for source in ready:
                    data = os.read(self.master, 4096) if source == self.master else self.peer.recv(4096)
                    if not data:
                        return
                    if source == self.master:
                        self.peer.sendall(data)
                    else:
                        os.write(self.master, data)
        except OSError:
            if not self.stopping.is_set():
                raise

    def close(self):
        self.stopping.set()
        self.thread.join(timeout=2)
        self.peer.close()
        os.close(self.master)
        os.close(self.slave)


def write_symbol_fixture(root):
    elf = root / "Game.elf"
    elf.write_bytes(b"fake ELF for identity tests")
    source = root / "source.json"
    source.write_text(json.dumps(dict(schemaVersion=2, methods=[dict(symbol="main", declaringType="Program", name="Main")])) )
    address = root / "addresses.json"
    address.write_text(json.dumps(dict(schemaVersion=1,
        elfSha256=hashlib.sha256(elf.read_bytes()).hexdigest(),
        sourceMapSha256=hashlib.sha256(source.read_bytes()).hexdigest(),
        methods=[dict(symbol="main", nativeStart="0x80004000", nativeSize=256, breakpoints=[
            dict(document="/build/日本/Game.cs", line=10, addresses=["0x80004004"]),
            dict(document="/build/日本/Game.cs", line=11, addresses=["0x80004008"]),
            dict(document="/build/日本/Game.cs", line=12, addresses=["0x8000400c"]),
            dict(document="/build/日本/Other.cs", line=5, addresses=["0x80004004"])])],
        unwind=[dict(start="0x80004000", end="0x80004100", supported=True,
                     cfaRegister=1, cfaOffset=0, registers={"65": dict(kind="undefined")})])))
    return elf, source, address


class AdapterTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.elf, self.source, self.address = write_symbol_fixture(self.root)
        self.client = probe.DapProcess(ADAPTER)
        self.target = None
        self.serial_relay = None
        self.client.request("initialize", dict(pathFormat="path", linesStartAt1=True), fragment=True)

    def tearDown(self):
        self.client.close()
        if self.serial_relay:
            self.serial_relay.close()
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

    def test_invalid_transport_is_rejected_before_connecting(self):
        for args in [dict(transport="unknown"), dict(transport="usbGecko", serialPort="", baudRate=115200),
                     dict(transport="usbGecko", serialPort="COM5", baudRate=0),
                     dict(transport="tcp", host="localhost", port=0), dict(timeoutSeconds=121)]:
            with self.subTest(args=args):
                self.client.request("attach", dict(elf=str(self.elf), sourceMap=str(self.source),
                    addressMap=str(self.address), **args), success=False)

    @unittest.skipUnless(sys.platform.startswith("linux"), "Linux pseudo-terminal serial transport check")
    def test_usb_attach_breakpoint_step_pause_and_disconnect_over_serial(self):
        self.target = Target()
        self.serial_relay = SerialRelay(self.target.port)
        self.client.request("attach", dict(transport="usbGecko", serialPort=self.serial_relay.path,
            baudRate=115200, timeoutSeconds=3, elf=str(self.elf), sourceMap=str(self.source),
            addressMap=str(self.address), sourceFileMap={"/build/日本": "/local/日本"}))
        self.client.event("initialized")
        self.bp(lines=(11,))
        self.client.request("configurationDone")
        self.client.event("stopped")
        self.client.request("continue", dict(threadId=1))
        self.assertEqual("breakpoint", self.client.event("stopped")["reason"])
        frames = self.client.request("stackTrace", dict(threadId=1))["body"]["stackFrames"]
        self.assertEqual(11, frames[0]["line"])
        self.client.request("stepIn", dict(threadId=1))
        self.assertEqual("SourceLocation", self.client.event("stopped")["description"])
        frames = self.client.request("stackTrace", dict(threadId=1))["body"]["stackFrames"]
        self.assertEqual(12, frames[0]["line"])
        self.bp(lines=())
        self.client.request("continue", dict(threadId=1))
        self.client.request("pause", dict(threadId=1))
        self.assertEqual("pause", self.client.event("stopped")["reason"])
        self.client.request("disconnect")
        self.client.event("terminated")
        self.assertEqual(0, self.client.process.wait(timeout=5))
        self.assertFalse(self.target.breakpoints)

    def test_serial_enumeration_is_json_without_dap_frames(self):
        result = subprocess.run(["dotnet", str(ADAPTER), "--list-serial-ports"],
                                capture_output=True, text=True, timeout=15, check=True)
        self.assertIsInstance(json.loads(result.stdout), list)

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

    def test_primitive_scopes_pagination_big_endian_and_stale_handles(self):
        data = json.loads(self.address.read_text())
        def variable(name, type, group, size, location, pcs=("0x80004008", "0x8000400c")):
            return dict(name=name, type=type, group=group, storageBytes=size,
                        location=location, visibleAddresses=pcs)
        data["methods"][0]["variables"] = [
            variable("value", "System.Int32", "Arguments", 4, dict(kind="cfaOffset", offset=4)),
            variable("signed", "System.Int32", "Locals", 4, dict(kind="cfaOffset", offset=8), ("0x8000400c",)),
            variable("fraction", "System.Double", "Locals", 8, dict(kind="cfaOffset", offset=16)),
            variable("optimized", "System.Int32", "Locals", 4, dict(kind="unavailable", reason="Unsupported expression")),
            variable("register", "System.UInt32", "Locals", 4, dict(kind="register", register=3)),
            variable("surrogate", "System.Char", "Locals", 4, dict(kind="cfaOffset", offset=24))]
        self.address.write_text(json.dumps(data))
        self.attach()
        self.bp(lines=(11,))
        self.client.request("continue", dict(threadId=1))
        self.client.event("stopped")
        frame = self.client.request("stackTrace", dict(threadId=1))["body"]["stackFrames"][0]
        scopes = self.client.request("scopes", dict(frameId=frame["id"]))["body"]["scopes"]
        refs = {s["name"]: s["variablesReference"] for s in scopes}
        value = self.client.request("variables", dict(variablesReference=refs["Arguments"]))["body"]["variables"][0]
        self.assertEqual(("value", "3", 0), (value["name"], value["value"], value["variablesReference"]))
        local = self.client.request("variables", dict(variablesReference=refs["Locals"]))["body"]["variables"]
        self.assertEqual(["fraction", "optimized", "register", "surrogate"], [v["name"] for v in local])
        self.assertEqual("3.5", local[0]["value"])
        self.assertIn("unavailable", local[1]["value"])
        self.assertEqual("42", local[2]["value"])
        self.assertEqual('"\\uD83D"', local[3]["value"])
        self.assertEqual(1, len(self.client.request("variables", dict(variablesReference=refs["Locals"], start=1, count=1))["body"]["variables"]))
        self.bp(lines=())
        self.client.request("stepIn", dict(threadId=1))
        self.client.event("stopped")
        self.client.request("variables", dict(variablesReference=refs["Arguments"]), success=False)
        frame = self.client.request("stackTrace", dict(threadId=1))["body"]["stackFrames"][0]
        scopes = self.client.request("scopes", dict(frameId=frame["id"]))["body"]["scopes"]
        ref = next(s["variablesReference"] for s in scopes if s["name"] == "Locals")
        values = self.client.request("variables", dict(variablesReference=ref))["body"]["variables"]
        self.assertEqual("-1", values[0]["value"])
        self.client.request("variables", dict(variablesReference=ref, start=-1), success=False)

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
