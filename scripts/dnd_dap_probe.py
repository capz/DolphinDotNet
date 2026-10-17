#!/usr/bin/env python3
"""Black-box DAP acceptance client. The smoke harness owns Dolphin, not this process."""
import argparse
import json
from pathlib import Path
import queue
import select
import socket
import subprocess
import sys
import threading
import time


class TelemetryRelay:
    """Keep the test's upstream socket open to inspect RAM after DAP cleanup.

    Forward bytes unchanged while the adapter owns the session. Only after its
    socket closes do we issue our own read; no second client races the adapter.
    """
    def __init__(self, target_port, address):
        self.address = address
        self.closed = threading.Event()
        self.finished = threading.Event()
        self.error = None
        self.trace = []
        deadline = time.monotonic() + 15
        while True:
            try:
                self.target = socket.create_connection(('127.0.0.1', target_port), timeout=.4)
                self.target.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
                break
            except OSError:
                if time.monotonic() >= deadline:
                    raise
                time.sleep(.15)
        self.listener = socket.socket()
        self.listener.bind(('127.0.0.1', 0))
        self.listener.listen(1)
        self.listener.settimeout(.2)
        self.port = self.listener.getsockname()[1]
        self.thread = threading.Thread(target=self._run, daemon=True)
        self.thread.start()

    def _run(self):
        try:
            deadline = time.monotonic() + 15
            while not self.closed.is_set():
                try:
                    client, _ = self.listener.accept()
                    client.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
                    break
                except socket.timeout:
                    if time.monotonic() >= deadline:
                        raise TimeoutError('Adapter did not connect to telemetry relay')
            else:
                return
            with client:
                while not self.closed.is_set():
                    ready, _, _ = select.select([self.target, client], [], [], .2)
                    disconnected = False
                    for source in ready:
                        data = source.recv(8192)
                        if not data:
                            if source is self.target:
                                raise EOFError('Target closed before telemetry inspection')
                            disconnected = True
                            break
                        destination = client if source is self.target else self.target
                        self.trace.append(f"{'target' if source is self.target else 'adapter'}: {data!r}")
                        self.trace = self.trace[-32:]
                        destination.sendall(data)
                    if disconnected:
                        self._verify_disconnected()
                        return
        except Exception as error:
            if not self.closed.is_set():
                self.error = error
        finally:
            self.target.close()
            self.listener.close()
            self.finished.set()

    def _verify_disconnected(self):
        command = f'm{self.address:x},8'.encode('ascii')
        self.target.sendall(b'$' + command + f'#{sum(command) & 255:02x}'.encode('ascii'))
        deadline = time.monotonic() + 5

        def byte():
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                raise TimeoutError('Telemetry memory readback timed out')
            self.target.settimeout(remaining)
            value = self.target.recv(1)
            if not value:
                raise EOFError('Target closed during telemetry readback')
            return value

        while (value := byte()) != b'$':
            if value not in (b'+', b'-'):
                raise ValueError(f'Invalid telemetry RSP framing: {value!r}')
        payload = bytearray()
        while (value := byte()) != b'#':
            if len(payload) >= 16:
                raise ValueError('Oversized telemetry memory response')
            payload.extend(value)
        checksum = byte() + byte()
        if int(checksum, 16) != (sum(payload) & 255):
            raise ValueError('Invalid telemetry memory checksum')
        self.target.sendall(b'+')
        if payload.lower() != b'444e443100000002':
            raise AssertionError(f'Disconnected telemetry not persisted: {payload!r}')

    def verify(self):
        if not self.finished.wait(7):
            raise TimeoutError('Adapter did not release telemetry connection')
        if self.error:
            raise self.error

    def close(self):
        self.closed.set()
        self.thread.join(timeout=6)


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


def require_source_step(stop):
    # 'step' alone also describes bounded/ambiguous stops, which must not be
    # accepted as successful source advancement. Retain the actual reason in CI.
    assert stop.get("reason") == "step" and stop.get("description") == "SourceLocation", stop



def primitive_values(client, frame):
    values = {}
    for scope in client.request("scopes", dict(frameId=frame["id"]))["body"]["scopes"]:
        for value in client.request("variables", dict(variablesReference=scope["variablesReference"]))["body"]["variables"]:
            values[value["name"]] = value["value"]
    return values


def object_values(client, frame):
    roots = {}
    for scope in client.request('scopes', dict(frameId=frame['id']))['body']['scopes']:
        roots.update({v['name']: v for v in client.request('variables', dict(variablesReference=scope['variablesReference']))['body']['variables']})
    assert roots['text']['value'] == '"hello"' and roots['caption']['value'] == '"hello"', roots
    for name in ('box', 'local'):
        reference = roots[name]['variablesReference']
        assert reference > 0, roots[name]
        fields = {v['name']: v for v in client.request('variables', dict(variablesReference=reference))['body']['variables']}
        assert {n: fields[n]['value'] for n in ('Count', 'Flag', 'Delta', 'Fraction', 'Name')} == {
            'Count': '7', 'Flag': '1', 'Delta': '-2', 'Fraction': '1.5', 'Name': '"Snake"'}, fields
        nested = client.request('variables', dict(variablesReference=fields['Next']['variablesReference']))['body']['variables']
        assert next(v for v in nested if v['name'] == 'Count')['value'] == '7', nested
    print('DND_DAP_OBJECT_FIELDS inherited/nested/scalar fields verified', flush=True)
    print('DND_DAP_STRING_PREVIEW "Snake" / "hello"', flush=True)

def run_probe(args):
    relay = None
    if args.telemetry:
        metadata = json.loads(args.address_map.read_text())['debuggerTelemetry']
        assert metadata['version'] == 1 and metadata['size'] == 8, metadata
        address = int(metadata['address'], 16)
        assert address % 4 == 0 and 0x80000000 <= address <= 0x817ffff8, metadata
        relay = TelemetryRelay(args.port, address)
    client = None
    try:
        client = DapProcess(args.adapter)
        client.request("initialize", dict(adapterID="dolphindotnet", linesStartAt1=True,
                                          columnsStartAt1=True, pathFormat="path"))
        attach = dict(host="127.0.0.1", port=relay.port if relay else args.port, elf=str(args.elf),
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
        for index, (path, line) in enumerate(specifications):
            response = client.request("setBreakpoints", dict(source=dict(path=path), breakpoints=[dict(line=int(line))]))
            assert all(b["verified"] for b in response["body"]["breakpoints"]), response
            client.request("continue", dict(threadId=1))
            stop = client.event("stopped")
            assert stop["reason"] == "breakpoint", stop
            frames = client.request("stackTrace", dict(threadId=1))["body"]["stackFrames"]
            assert frames and frames[0]["line"] == int(line), frames
            print(f"DND_DAP_SOURCE_BREAKPOINT {path}:{line}", flush=True)
            client.request("setBreakpoints", dict(source=dict(path=path), breakpoints=[]))
            if not args.stack and not args.inspection and index == 0:
                # Step from the first short statement, before the next statement
                # enters console/rendering code with an intentionally bounded step budget.
                client.request("stepIn", dict(threadId=1))
                require_source_step(client.event("stopped"))
                stepped = client.request("stackTrace", dict(threadId=1))["body"]["stackFrames"]
                assert stepped and stepped[0]["source"] and stepped[0]["line"] > 0, stepped
                print("DND_DAP_SOURCE_STEP", flush=True)
        if args.inspection:
            object_values(client, frames[0])
        if args.stack:
            names = [frame["name"] for frame in frames]
            assert names[:3] == ["Program.Inner", "Program.Outer", "Program.Main"], names
            print("DND_DAP_MANAGED_STACK " + " -> ".join(names[:3]), flush=True)
            values = primitive_values(client, frames[0])
            assert values.get("value") == "3", values
            print("DND_DAP_ARGUMENT value=3", flush=True)
            client.request("stepOut", dict(threadId=1))
            require_source_step(client.event("stopped"))
            frames = client.request("stackTrace", dict(threadId=1))["body"]["stackFrames"]
            assert frames[0]["name"] == "Program.Outer" and frames[0]["line"] in (13, 14), frames
            print(f"DND_DAP_STEP_OUT Program.Outer:{frames[0]['line']}", flush=True)
            if frames[0]["line"] == 13:
                client.request("next", dict(threadId=1))
                require_source_step(client.event("stopped"))
                frames = client.request("stackTrace", dict(threadId=1))["body"]["stackFrames"]
            assert frames[0]["name"] == "Program.Outer" and frames[0]["line"] == 14, frames
            values = primitive_values(client, frames[0])
            assert values.get("first") == "4", values
            print("DND_DAP_LOCAL first=4", flush=True)
            client.request("next", dict(threadId=1))
            require_source_step(client.event("stopped"))
            frames = client.request("stackTrace", dict(threadId=1))["body"]["stackFrames"]
            assert frames[0]["name"] == "Program.Outer" and frames[0]["line"] == 15, frames
            print("DND_DAP_STEP_OVER Program.Outer:15", flush=True)
        client.request("continue", dict(threadId=1))
        time.sleep(.2)
        client.request("pause", dict(threadId=1))
        assert client.event("stopped")["reason"] == "pause"
        print("DND_DAP_PAUSE", flush=True)
        client.request("disconnect", dict(terminateDebuggee=False))
        client.event("terminated")
        assert client.process.wait(timeout=5) == 0
        if relay:
            relay.verify()
            print('DND_DAP_DEBUGGER_TELEMETRY disconnected state verified in target RAM', flush=True)
    except Exception:
        if relay:
            print('Recent telemetry relay traffic:\n' + '\n'.join(relay.trace), file=sys.stderr)
        raise
    finally:
        if client:
            client.close()
        if relay:
            relay.close()


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--adapter", type=Path, required=True)
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--elf", type=Path, required=True)
    parser.add_argument("--source-map", type=Path, required=True)
    parser.add_argument("--address-map", type=Path, required=True)
    parser.add_argument("--stack", action="store_true")
    parser.add_argument("--inspection", action="store_true")
    parser.add_argument("--telemetry", action="store_true")
    parser.add_argument("sources", nargs="+")
    run_probe(parser.parse_args())
