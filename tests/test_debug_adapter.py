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
import struct
import shutil
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
    def __init__(self, startup=False, interrupt_empty_reply=False):
        self.listener = socket.socket()
        self.listener.bind(("127.0.0.1", 0))
        self.listener.listen()
        self.port = self.listener.getsockname()[1]
        self.pc = 0x80003100 if startup else 0x80004000
        self.stack_pointer = 0 if startup else 0x81700000
        self.interrupt_empty_reply = interrupt_empty_reply
        self.duplicate_interrupt_stop = False
        self.delayed_interrupt_stop = None
        self.empty_register = False
        self.step_reply = None
        self.step_destinations = {}
        self.step_stack_pointers = {}
        self.continue_stop = None
        self.continue_stack_pointer = None
        self.continuing = threading.Event()
        self.delay_memory = False
        self.memory_requested = threading.Event()
        self.memory_release = threading.Event()
        self.interrupts = 0
        self.refused_memory = set()
        self.refused_writes = False
        self.ignored_writes = False
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
                        self.interrupts += 1
                        if self.interrupt_empty_reply or self.duplicate_interrupt_stop:
                            # Dolphin sends a stop without an ACK for raw Ctrl-C,
                            # then an unsolicited empty reply from its dispatcher.
                            payload = f"T0540:{self.pc:08x};".encode()
                            packet = b"$" + payload + f"#{sum(payload) & 255:02x}".encode()
                            self.peer.sendall(packet + (packet if self.duplicate_interrupt_stop else b'') +
                                              (b"$#00" if self.interrupt_empty_reply else b''))
                        else:
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
                        if self.step_reply is not None:
                            self.reply(self.step_reply)
                            continue
                        self.stack_pointer = self.step_stack_pointers.get(self.pc, self.stack_pointer)
                        self.pc = self.step_destinations.get(self.pc, self.pc + 4)
                        self.reply(f"T0540:{self.pc:08x};")
                    elif command == "c":
                        self.continuing.set()
                        if self.continue_stop is not None:
                            reply = self.continue_stop
                            self.continue_stop = None
                            if reply.startswith('T'):
                                self.pc = int(reply.split(':')[1].split(';')[0], 16)
                            if self.continue_stack_pointer is not None:
                                self.stack_pointer = self.continue_stack_pointer
                            self.reply(reply)
                            continue
                        candidates = sorted(b for b in self.breakpoints if b >= self.pc)
                        if candidates:
                            self.pc = candidates[0]
                            self.stack_pointer = 0x81700000
                            self.reply(f"T0540:{self.pc:08x};")
                        else:
                            self.peer.sendall(b"+")
                    elif command == "p1":
                        self.reply('' if self.empty_register else f"{self.stack_pointer:08x}")
                    elif command == "p3":
                        self.reply("0000002a")
                    elif command.startswith('M'):
                        if self.refused_writes:
                            self.reply('')  # Valid RSP unsupported-command response.
                            continue
                        if self.ignored_writes:
                            self.reply('OK')
                            continue
                        location, encoded = command[1:].split(':')
                        address, size = (int(v, 16) for v in location.split(','))
                        value = bytes.fromhex(encoded)
                        assert len(value) == size
                        for base, block in list(self.memory.items()):
                            if base <= address and address + size <= base + len(block):
                                start = address - base
                                self.memory[base] = block[:start] + value + block[start+size:]
                        self.memory[address] = value
                        self.reply('OK')
                    elif command.startswith("m"):
                        if self.delay_memory:
                            self.memory_requested.set()
                            assert self.memory_release.wait(3), 'Timed out waiting for memory response release'
                            self.delay_memory = False
                        address, size = (int(v, 16) for v in command[1:].split(","))
                        if address in self.refused_memory:
                            self.reply('E01')
                            continue
                        self.reply(self.memory.get(address, bytes(size))[:size].hex())
                        if self.delayed_interrupt_stop and self.interrupts:
                            payload = self.delayed_interrupt_stop.encode()
                            self.peer.sendall(b'$' + payload + f'#{sum(payload) & 255:02x}'.encode())
                            self.delayed_interrupt_stop = None
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


def write_inspection_fixture(addresses):
    data = json.loads(addresses.read_text())
    fields = [dict(name=name, type=type, offset=offset, storageBytes=width, valueKind=kind)
        for name, type, offset, width, kind in [
            ('Count', 'System.Int32', 8, 4, 'primitive'), ('Flag', 'System.Byte', 12, 1, 'primitive'),
            ('Delta', 'System.Int16', 14, 2, 'primitive'), ('Fraction', 'System.Double', 16, 8, 'primitive'),
            ('Name', 'System.String', 24, 4, 'reference'), ('Next', 'Box', 28, 4, 'reference')]]
    data['inspection'] = dict(version=1, pointerSize=4, objectHeaderSize=8, stringLengthOffset=8, stringDataOffset=12,
        types=[dict(name='Box', kind='object', address='0x80008000', size=32, fields=fields),
               dict(name='System.String', kind='string', address='0x80008040', size=12, fields=[])])
    data['methods'][0]['variables'] = [dict(name=name, type=type, group='Locals', storageBytes=4,
        valueKind='reference', location=dict(kind='cfaOffset', offset=offset), visibleAddresses=['0x80004008', '0x8000400c'])
        for name, type, offset in [('box', 'Box', 4), ('text', 'System.String', 8), ('nothing', 'Box', 12)]]
    addresses.write_text(json.dumps(data))
    return {0x81700004: bytes.fromhex('80010000'), 0x81700008: bytes.fromhex('80011000'), 0x8170000c: bytes(4),
            0x80010000: bytes.fromhex('80008000'), 0x80010008: bytes.fromhex('00000007'),
            0x8001000c: bytes.fromhex('ff'), 0x8001000e: bytes.fromhex('fffe'),
            0x80010010: bytes.fromhex('3ff8000000000000'), 0x80010018: bytes.fromhex('80011000'),
            0x8001001c: bytes.fromhex('80010000'), 0x80011000: bytes.fromhex('80008040'),
            0x80011008: bytes.fromhex('00000005'), 0x8001100c: 'Snake'.encode('utf-16-be')}


class AdapterTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.elf, self.source, self.address = write_symbol_fixture(self.root)
        self.client = probe.DapProcess(ADAPTER)
        self.target = None
        self.serial_relay = None
        self.telemetry_relay = None
        self.client.request("initialize", dict(pathFormat="path", linesStartAt1=True, supportsVariablePaging=True), fragment=True)

    def tearDown(self):
        if self.target:
            self.target.memory_release.set()
        self.client.close()
        if self.serial_relay:
            self.serial_relay.close()
        if self.telemetry_relay:
            self.telemetry_relay.close()
        if self.target:
            self.target.thread.join(timeout=2)
            self.assertFalse(self.target.errors, self.target.errors)
        self.temp.cleanup()

    def attach(self, configured=True, interrupt_empty_reply=False, target=None, heartbeat=None):
        self.target = target or Target(interrupt_empty_reply=interrupt_empty_reply)
        self.client.request("attach", dict(host="127.0.0.1", port=self.target.port,
            elf=str(self.elf), sourceMap=str(self.source), addressMap=str(self.address),
            sourceFileMap={"/build/日本": "/local/日本"}, **(heartbeat or {})))
        self.client.event("initialized")
        if configured:
            self.client.request("configurationDone")
            self.client.event("stopped")

    def bp(self, path="Game.cs", lines=(10,)):
        return self.client.request("setBreakpoints", dict(source=dict(path="/local/日本/" + path),
            breakpoints=[dict(line=line) for line in lines]))["body"]["breakpoints"]

    def test_breakpoint_changes_while_running_wait_for_stop_and_report_verification(self):
        self.attach()
        self.client.request('continue', dict(threadId=1))
        self.assertTrue(self.target.continuing.wait(2))
        commands = list(self.target.commands)
        pending = self.bp(lines=(11,))
        self.assertFalse(pending[0]['verified'])
        self.assertIn('next stop', pending[0]['message'])
        self.assertEqual(commands, self.target.commands)
        self.client.request('pause', dict(threadId=1))
        changed = self.client.event('breakpoint')
        self.assertEqual('changed', changed['reason'])
        self.assertTrue(changed['breakpoint']['verified'])
        self.assertEqual(pending[0]['id'], changed['breakpoint']['id'])
        self.client.event('stopped')
        self.assertTrue(self.target.breakpoints)

    def test_latest_running_breakpoint_replacement_can_cancel_an_addition(self):
        self.attach()
        self.client.request('continue', dict(threadId=1))
        self.assertTrue(self.target.continuing.wait(2))
        self.bp(lines=(11,))
        self.assertEqual([], self.bp(lines=()))
        self.client.request('pause', dict(threadId=1))
        self.client.event('stopped')
        self.assertEqual(set(), self.target.breakpoints)

    def telemetry_target(self, signature='444e443100000000', refused=False):
        data = json.loads(self.address.read_text())
        data['debuggerTelemetry'] = dict(version=1, size=8, address='0x80009000')
        self.address.write_text(json.dumps(data))
        target = Target()
        target.memory[0x80009000] = bytes.fromhex(signature)
        target.refused_writes = refused
        return target

    def heartbeat_target(self, supported=True):
        target = self.telemetry_target()
        data = json.loads(self.address.read_text())
        data['debuggerHeartbeat'] = dict(version=1, size=24, supported=supported, address='0x80009100')
        self.address.write_text(json.dumps(data))
        target.memory[0x80009100] = bytes.fromhex('444e4842') + bytes(20)
        return target

    def live_attach(self):
        self.attach(target=self.heartbeat_target(), heartbeat=dict(heartbeatHost='127.0.0.1', heartbeatPort=0))
        slot = self.target.memory[0x80009100]
        magic, host, port, high, low, enabled = struct.unpack('>6I', slot)
        self.assertEqual((0x444e4842, 0x7f000001, 1), (magic, host, enabled))
        self.assertNotEqual(0, high | low)
        return ('127.0.0.1', port), struct.pack('>5I', magic, 1, high, low, 1)

    def test_live_heartbeat_runs_without_interrupting_or_writing_gdb_while_running(self):
        endpoint, ping = self.live_attach()
        self.client.request('continue', dict(threadId=1))
        self.assertTrue(self.target.continuing.wait(2))
        commands = list(self.target.commands)
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as udp:
            udp.settimeout(.15)
            invalid = [ping[:19], ping+b'x', ping[:8]+bytes(8)+ping[16:], ping[:4]+bytes(4)+ping[8:], ping[:16]+bytes(4)]
            for packet in invalid:
                udp.sendto(packet, endpoint)
                with self.assertRaises(socket.timeout): udp.recvfrom(64)
            udp.sendto(ping, endpoint)
            reply, peer = udp.recvfrom(64)
            self.assertEqual(ping[:4]+struct.pack('>I', 2)+ping[8:], reply)
            self.assertEqual(endpoint, peer)
            self.assertEqual(commands, self.target.commands)
            self.assertEqual(0, self.target.interrupts)
            self.client.request('disconnect')
            self.client.event('terminated')
            udp.sendto(ping, endpoint)
            with self.assertRaises(OSError): udp.recvfrom(64)

    def test_live_heartbeat_stops_after_adapter_crash_without_a_clean_status_write(self):
        endpoint, ping = self.live_attach()
        self.client.request('continue', dict(threadId=1))
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as udp:
            udp.settimeout(.2)
            udp.sendto(ping, endpoint)
            self.assertEqual(20, len(udp.recvfrom(64)[0]))
            self.client.process.kill(); self.client.process.wait(timeout=3)
            self.assertEqual(1, struct.unpack('>I', self.target.memory[0x80009000][4:])[0])
            udp.sendto(ping, endpoint)
            with self.assertRaises(OSError): udp.recvfrom(64)

    def test_live_heartbeat_declines_paused_sessions_and_buffered_stop_on_peer_loss(self):
        endpoint, ping = self.live_attach()
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as udp:
            udp.settimeout(.2)
            udp.sendto(ping, endpoint)
            with self.assertRaises(socket.timeout): udp.recvfrom(64)
            self.client.request('continue', dict(threadId=1))
            udp.sendto(ping, endpoint); self.assertEqual(20, len(udp.recvfrom(64)[0]))
            self.client.request('pause', dict(threadId=1)); self.client.event('stopped')
            payload = b'T0540:80004004;'
            self.target.peer.sendall(b'$'+payload+f'#{sum(payload)&255:02x}'.encode())
            self.target.peer.shutdown(socket.SHUT_RDWR)
            self.target.thread.join(timeout=2)
            udp.sendto(ping, endpoint)
            with self.assertRaises(socket.timeout): udp.recvfrom(64)

    def test_attach_without_live_options_clears_stale_heartbeat_configuration(self):
        target = self.heartbeat_target()
        target.memory[0x80009100] = bytes.fromhex('444e4842') + bytes.fromhex('01010101')*5
        self.attach(target=target)
        self.assertEqual(bytes(20), target.memory[0x80009100][4:])

    def test_native_bba_gdb_stack_explicitly_declines_live_udp(self):
        self.attach(target=self.heartbeat_target(supported=False), heartbeat=dict(heartbeatHost='127.0.0.1', heartbeatPort=0))
        self.assertIn('native BBA GDB stack', self.client.event('output')['output'])
        self.assertEqual(bytes(20), self.target.memory[0x80009100][4:])

    def test_older_status_maps_keep_debugging_when_live_status_is_requested(self):
        self.attach(target=self.telemetry_target(), heartbeat=dict(heartbeatHost='127.0.0.1', heartbeatPort=0))
        self.assertIn('rebuild the target', self.client.event('output')['output'])
        self.assertEqual(['M80009004,4:00000001'], [c for c in self.target.commands if c.startswith('M')])

    def test_live_udp_bind_failure_clears_stale_configuration_and_keeps_attach(self):
        target = self.heartbeat_target()
        target.memory[0x80009100] = bytes.fromhex('444e4842') + bytes.fromhex('01010101')*5
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as occupied:
            occupied.bind(('0.0.0.0', 0))
            self.attach(target=target, heartbeat=dict(heartbeatHost='127.0.0.1', heartbeatPort=occupied.getsockname()[1]))
        self.assertIn('Live debugger status unavailable', self.client.event('output')['output'])
        self.assertEqual(bytes(20), target.memory[0x80009100][4:])
        self.bp(lines=(11,));self.client.request('continue', dict(threadId=1))
        self.assertEqual('breakpoint', self.client.event('stopped')['reason'])

    def test_live_endpoint_validation_precedes_transport_connection(self):
        base = dict(host='127.0.0.1', port=1, elf=str(self.elf), sourceMap=str(self.source), addressMap=str(self.address))
        for options in [dict(heartbeatHost='localhost'), dict(heartbeatHost='224.0.0.1'),
                        dict(heartbeatHost='192.168.1.999'), dict(heartbeatHost='0.0.0.0'),
                        dict(heartbeatHost='127.0.0.1', heartbeatPort=65536), dict(heartbeatPort=2167)]:
            with self.subTest(options=options):
                response = self.client.request('attach', dict(base, **options), success=False)
                self.assertIn('heartbeat', response['message'])

    def test_heartbeat_metadata_refuses_overlap_and_incompatible_slots(self):
        base = json.loads(self.address.read_text())
        base['debuggerTelemetry'] = dict(version=1, size=8, address='0x80009000')
        for options in [dict(version=1, size=24, supported=True, address='0x80009004'),
                        dict(version=2, size=24, supported=True, address='0x80009100'),
                        dict(version=1, size=8, supported=True, address='0x80009100'),
                        dict(version=1, size=24, supported=True, address='0x817ffff0')]:
            with self.subTest(options=options):
                self.address.write_text(json.dumps(dict(base, debuggerHeartbeat=options)))
                response = self.client.request('attach', dict(host='127.0.0.1', port=1,
                    elf=str(self.elf), sourceMap=str(self.source), addressMap=str(self.address)), success=False)
                self.assertIn('heartbeat', response['message'])

    @unittest.skipUnless(sys.platform.startswith('linux') and shutil.which('cc'), 'Requires POSIX C host fixture')
    def test_real_native_guest_udp_detects_adapter_crash_and_new_session_recovery(self):
        root = ADAPTER.parents[5]
        binary = self.root / 'heartbeat-guest'
        subprocess.run(['cc', '-std=c11', '-Wall', '-Wextra', '-Werror', '-DDND_DEBUG_TELEMETRY', '-I'+str(root/'include'),
            str(root/'tests/debug_heartbeat_host.c'), str(root/'source/debug_heartbeat.c'), str(root/'source/performance.c'),
            str(root/'source/network_posix.c'), '-Wl,--wrap=dnd_np_monitor_open', '-o', str(binary)], check=True, capture_output=True)
        self.live_attach()
        self.client.request('continue', dict(threadId=1))
        guest = subprocess.Popen([str(binary)], stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True)
        def command(text):
            guest.stdin.write(text+'\n');guest.stdin.flush()
            self.assertTrue(select.select([guest.stdout], [], [], 4)[0], 'Native heartbeat fixture response deadline')
            return guest.stdout.readline().strip()
        def configure():
            _, host, port, high, low, _ = struct.unpack('>6I', self.target.memory[0x80009100])
            self.assertEqual('configured', command(f'c {host} {port} {high} {low}'))
        try:
            configure()
            self.assertEqual('1', command('t 0 1 1'))
            self.assertEqual('1', command('w'))
            self.assertEqual('2', command('t 2000000 1 1'))
            self.client.process.kill();self.client.process.wait(timeout=3)
            self.target.thread.join(timeout=2)
            for clock in (4000000,6000000,8000000,10000000): state=command(f't {clock} 1 1')
            self.assertEqual('3', state) # No graceful disconnect report was written.
            self.client.close()
            self.client = probe.DapProcess(ADAPTER)
            self.client.request('initialize', dict(pathFormat='path', linesStartAt1=True))
            self.live_attach();self.client.request('continue', dict(threadId=1));configure()
            self.assertEqual('1', command('t 12000000 1 1'))
            self.assertEqual('1', command('w'))
            self.assertEqual('2', command('t 14000000 1 1'))
            self.assertEqual('3', command('t 16000000 1 0')) # Physical cable loss.
        finally:
            guest.stdin.close()
            try: self.assertEqual(0, guest.wait(timeout=3))
            except subprocess.TimeoutExpired: guest.kill();guest.wait();raise
            guest.stdout.close()

    def test_overlay_telemetry_reports_attach_and_clean_disconnect_only(self):
        self.attach(target=self.telemetry_target())
        self.assertEqual(bytes.fromhex('444e443100000001'), self.target.memory[0x80009000])
        self.client.request('continue', dict(threadId=1))
        self.client.request('disconnect')
        self.client.event('terminated')
        self.assertEqual(bytes.fromhex('444e443100000002'), self.target.memory[0x80009000])
        self.assertEqual(['M80009004,4:00000001', 'M80009004,4:00000002'], [c for c in self.target.commands if c.startswith('M')])
        self.assertNotIn('k', self.target.commands)

    def test_overlay_telemetry_unsupported_write_does_not_break_debugging(self):
        self.attach(target=self.telemetry_target(refused=True))
        self.assertIn('Debugger overlay status unavailable', self.client.event('output')['output'])
        self.bp(lines=(11,))
        self.client.request('continue', dict(threadId=1))
        self.assertEqual('breakpoint', self.client.event('stopped')['reason'])

    def test_overlay_telemetry_acknowledged_but_ignored_write_is_unavailable(self):
        target = self.telemetry_target()
        target.ignored_writes = True
        self.attach(target=target)
        self.assertIn('write did not persist', self.client.event('output')['output'])
        self.client.request('disconnect')
        self.client.event('terminated')
        self.assertEqual(['M80009004,4:00000001'], [c for c in target.commands if c.startswith('M')])

    def test_emulator_acceptance_relay_reads_persisted_disconnect_after_adapter_releases_socket(self):
        target = self.telemetry_target()
        target.interrupt_empty_reply = True
        target.delayed_interrupt_stop = 'T0540:80004004;'
        self.telemetry_relay = probe.TelemetryRelay(target.port, 0x80009000)
        target.port = self.telemetry_relay.port
        self.attach(target=target)
        self.client.request('continue', dict(threadId=1))
        self.client.request('disconnect')
        self.client.event('terminated')
        self.telemetry_relay.verify()
        self.assertEqual('m80009000,8', target.commands[-1])

    def test_pause_duplicate_stop_and_empty_reply_do_not_corrupt_inspection(self):
        target = Target(interrupt_empty_reply=True)
        target.duplicate_interrupt_stop = True
        self.attach(target=target)
        self.client.request('continue', dict(threadId=1))
        self.client.request('pause', dict(threadId=1))
        self.assertEqual('pause', self.client.event('stopped')['reason'])
        frames = self.client.request('stackTrace', dict(threadId=1))['body']['stackFrames']
        self.assertTrue(frames)
        self.client.request('disconnect')
        self.client.event('terminated')

    def test_emulator_acceptance_relay_rejects_missing_disconnect_report(self):
        target = self.telemetry_target(refused=True)
        self.telemetry_relay = probe.TelemetryRelay(target.port, 0x80009000)
        target.port = self.telemetry_relay.port
        self.attach(target=target)
        self.client.request('disconnect')
        self.client.event('terminated')
        with self.assertRaisesRegex(AssertionError, 'not persisted'):
            self.telemetry_relay.verify()

    def test_overlay_signature_mismatch_never_writes_target_memory(self):
        self.attach(target=self.telemetry_target(signature='1234567800000000'))
        self.assertIn('unsupported signature', self.client.event('output')['output'])
        self.assertFalse(any(c.startswith('M') for c in self.target.commands))

    def test_old_maps_do_not_write_overlay_telemetry(self):
        self.attach()
        self.client.request('disconnect')
        self.client.event('terminated')
        self.assertFalse(any(c.startswith('M') for c in self.target.commands))

    def attach_inspection(self, configure=None):
        memory = write_inspection_fixture(self.address)
        if configure:
            data = json.loads(self.address.read_text())
            configure(data)
            self.address.write_text(json.dumps(data))
        self.attach()
        self.target.memory.update(memory)
        self.bp(lines=(11,))
        self.client.request('continue', dict(threadId=1))
        self.client.event('stopped')
        frame = self.client.request('stackTrace', dict(threadId=1))['body']['stackFrames'][0]
        return self.client.request('scopes', dict(frameId=frame['id']))['body']['scopes'][0]['variablesReference']

    def variables(self, reference, **args):
        return self.client.request('variables', dict(variablesReference=reference, **args))['body']['variables']

    def test_objects_expand_lazily_with_scalar_fields_strings_null_and_stale_handles(self):
        scope = self.attach_inspection()
        roots = {v['name']: v for v in self.variables(scope)}
        self.assertEqual('"Snake"', roots['text']['value'])
        self.assertEqual('null', roots['nothing']['value'])
        box = roots['box']['variablesReference']
        self.assertGreater(box, 0)
        self.assertNotIn('m80010008,4', self.target.commands)
        fields = {v['name']: v for v in self.variables(box)}
        self.assertEqual(['7', '255', '-2', '1.5', '"Snake"'], [fields[n]['value'] for n in ('Count', 'Flag', 'Delta', 'Fraction', 'Name')])
        self.assertGreater(fields['Next']['variablesReference'], 0)
        self.assertTrue(all(v['presentationHint']['attributes'] == ['readOnly'] for v in fields.values()))
        self.assertEqual(['Delta'], [v['name'] for v in self.variables(box, start=2, count=1)])
        self.assertEqual([], self.variables(box, filter='indexed'))
        self.client.request('stepIn', dict(threadId=1))
        self.client.event('stopped')
        self.client.request('variables', dict(variablesReference=box), success=False)

    def test_cyclic_object_expansion_has_a_depth_limit(self):
        scope = self.attach_inspection()
        box = self.variables(scope)[0]
        for _ in range(4):
            self.assertGreater(box['variablesReference'], 0)
            box = next(v for v in self.variables(box['variablesReference']) if v['name'] == 'Next')
        self.assertEqual(0, box['variablesReference'])
        self.assertIn('maximum depth', box['value'])

    def test_string_preview_is_bounded_and_preserves_utf16_units(self):
        scope = self.attach_inspection()
        text = '日\n"\\\ud800' + 'a' * 140
        self.target.memory[0x80011008] = len(text).to_bytes(4, 'big')
        self.target.memory[0x8001100c] = text.encode('utf-16-be', errors='surrogatepass')
        preview = self.variables(scope)[1]['value']
        self.assertIn('日\\n\\"\\\\\\uD800', preview)
        self.assertIn('length=145', preview)
        self.assertIn('m8001100c,100', self.target.commands)
        self.assertNotIn('m8001100c,122', self.target.commands)

    def test_invalid_reference_and_unknown_runtime_type_do_not_guess_layouts(self):
        scope = self.attach_inspection()
        self.target.memory[0x81700004] = bytes.fromhex('12345678')
        self.target.memory[0x80011000] = bytes.fromhex('80008100')
        values = self.variables(scope)
        self.assertIn('Invalid object reference', values[0]['value'])
        self.assertIn('no verified layout', values[1]['value'])
        self.assertTrue(all(v['variablesReference'] == 0 for v in values))
        self.assertFalse(any(c.startswith('m12345678') for c in self.target.commands))

    def test_corrupt_string_length_is_unavailable_without_body_read(self):
        scope = self.attach_inspection()
        self.target.memory[0x80011008] = bytes.fromhex('ffffffff')
        self.assertIn('Invalid string length', self.variables(scope)[1]['value'])
        self.assertFalse(any(c.startswith('m8001100c,') for c in self.target.commands))

    def test_target_refused_field_is_unavailable_and_other_fields_still_work(self):
        scope = self.attach_inspection()
        box = self.variables(scope)[0]['variablesReference']
        self.target.refused_memory.add(0x80010008)
        fields = {v['name']: v for v in self.variables(box)}
        self.assertIn('Target refused memory read', fields['Count']['value'])
        self.assertEqual('255', fields['Flag']['value'])
        self.assertTrue(self.client.request('threads')['success'])

    def test_malformed_object_metadata_is_rejected_before_connecting(self):
        write_inspection_fixture(self.address)
        data = json.loads(self.address.read_text())
        data['inspection']['types'][0]['fields'][0]['offset'] = 32
        self.address.write_text(json.dumps(data))
        response = self.client.request('attach', dict(host='127.0.0.1', port=1, elf=str(self.elf),
            sourceMap=str(self.source), addressMap=str(self.address)), success=False)
        self.assertIn('Invalid inspection field layout', response['message'])

    def test_large_object_fields_are_paged_and_reads_are_bounded(self):
        def configure(data):
            layout = data['inspection']['types'][0]
            layout['size'] = 137
            layout['fields'] = [dict(name=f'f{i}', type='System.Byte', offset=8+i, storageBytes=1, valueKind='primitive') for i in range(129)]
        scope = self.attach_inspection(configure)
        box = self.variables(scope)[0]
        self.assertEqual(129, box['namedVariables'])
        self.assertEqual(128, len(self.variables(box['variablesReference'], count=10000)))
        self.assertNotIn('m80010088,1', self.target.commands)
        last = self.variables(box['variablesReference'], start=128, count=1)
        self.assertEqual(['f128'], [v['name'] for v in last])

    def test_client_without_variable_paging_can_expand_every_field(self):
        self.client.close()
        self.client = probe.DapProcess(ADAPTER)
        self.client.request('initialize', dict(clientID='visualstudio', pathFormat='path', supportsVariablePaging=False))
        def configure(data):
            layout = data['inspection']['types'][0]
            layout['size'] = 137
            layout['fields'] = [dict(name=f'f{i}', type='System.Byte', offset=8+i, storageBytes=1, valueKind='primitive') for i in range(129)]
        box = self.variables(self.attach_inspection(configure))[0]
        self.assertEqual(2, box['namedVariables'])
        groups = self.variables(box['variablesReference'])
        self.assertEqual(['Variables 1–128', 'Variables 129–129'], [g['name'] for g in groups])
        self.assertNotIn('m80010088,1', self.target.commands)
        self.assertEqual(128, len(self.variables(groups[0]['variablesReference'])))
        self.assertEqual(['f127'], [v['name'] for v in self.variables(groups[0]['variablesReference'], start=127, count=2)])
        self.assertEqual([], self.variables(groups[0]['variablesReference'], start=128))
        self.assertEqual(['f128'], [v['name'] for v in self.variables(groups[1]['variablesReference'])])
        stale = groups[1]['variablesReference']
        self.client.request('continue', dict(threadId=1))
        self.assertTrue(self.target.continuing.wait(2))
        self.client.request('pause', dict(threadId=1))
        self.client.event('stopped')
        self.client.request('variables', dict(variablesReference=stale), success=False)

    def test_unsupported_struct_field_does_not_read_guessed_storage(self):
        def configure(data):
            field = data['inspection']['types'][0]['fields'][0]
            field.update(type='SomeStruct', valueKind='unavailable')
        scope = self.attach_inspection(configure)
        box = self.variables(scope)[0]['variablesReference']
        self.assertIn('Unsupported field type', self.variables(box)[0]['value'])
        self.assertNotIn('m80010008,4', self.target.commands)

    def test_array_reference_is_explicitly_unsupported(self):
        scope = self.attach_inspection(lambda data: data['methods'][0]['variables'][0].update(type='System.Int32[]'))
        self.assertIn('Array inspection is not implemented', self.variables(scope)[0]['value'])
        self.assertNotIn('m80010000,4', self.target.commands)

    def test_reference_in_old_map_without_inspection_metadata_is_unavailable(self):
        scope = self.attach_inspection(lambda data: data.pop('inspection'))
        root = self.variables(scope)[0]
        self.assertIn('no verified layout', root['value'])
        self.assertEqual(0, root['variablesReference'])

    def test_native_startup_without_stack_remains_resumable(self):
        self.target = Target(startup=True)
        self.client.request('attach', dict(host='127.0.0.1', port=self.target.port,
            elf=str(self.elf), sourceMap=str(self.source), addressMap=str(self.address),
            sourceFileMap={'/build/日本': '/local/日本'}))
        self.client.event('initialized')
        self.assertTrue(self.bp(lines=(11,))[0]['verified'])
        self.client.request('configurationDone')
        entry = self.client.event('stopped')
        self.assertEqual('entry', entry['reason'])
        self.assertIn('Press Resume', entry['description'])
        frames = self.client.request('stackTrace', dict(threadId=1))['body']['stackFrames']
        self.assertEqual(1, len(frames))
        frame = frames[0]
        self.assertEqual('native 0x80003100', frame['name'])
        self.assertEqual('0x80003100', frame['instructionPointerReference'])
        self.assertEqual(0, frame['line'])
        self.assertIsNone(frame['source'])
        self.assertEqual([], self.client.request('scopes', dict(frameId=frame['id']))['body']['scopes'])
        # A native startup stop does not auto-run or read a guessed stack.
        self.assertNotIn('c', self.target.commands)
        self.assertFalse(any(c.startswith('m') for c in self.target.commands))
        self.client.request('continue', dict(threadId=1))
        self.assertEqual('breakpoint', self.client.event('stopped')['reason'])
        managed = self.client.request('stackTrace', dict(threadId=1))['body']['stackFrames'][0]
        self.assertEqual('Program.Main', managed['name'])
        self.assertEqual(11, managed['line'])
        self.assertEqual('/local/日本/Game.cs', managed['source']['path'])

    def test_dolphin_pause_extra_empty_reply_does_not_corrupt_register_reads(self):
        self.attach(interrupt_empty_reply=True)
        self.client.request('continue', dict(threadId=1))
        self.client.request('pause', dict(threadId=1))
        self.assertEqual('pause', self.client.event('stopped')['reason'])
        frame = self.client.request('stackTrace', dict(threadId=1))['body']['stackFrames'][0]
        self.assertEqual('Program.Main', frame['name'])
        self.assertTrue(self.bp(lines=(11,))[0]['verified'])
        self.client.request('continue', dict(threadId=1))
        self.assertEqual('breakpoint', self.client.event('stopped')['reason'])
        self.assertEqual(11, self.client.request('stackTrace', dict(threadId=1))['body']['stackFrames'][0]['line'])

    def test_empty_register_reply_after_command_ack_is_still_an_error(self):
        self.attach()
        self.client.request('continue', dict(threadId=1))
        self.client.request('pause', dict(threadId=1))
        self.client.event('stopped')
        self.target.empty_register = True
        result = self.client.request('stackTrace', dict(threadId=1), success=False)
        self.assertIn('Invalid register response', result['message'])
        self.client.event('terminated')
        self.client.request('threads', success=False)

    def attach_call_target(self, opcode=0x48001001, same_line=False):
        data = json.loads(self.address.read_text())
        data['methods'][0]['breakpoints'] = [
            dict(document='/build/日本/Game.cs', line=10, addresses=['0x80004000']),
            dict(document='/build/日本/Game.cs', line=11, addresses=['0x80004004']),
            dict(document='/build/日本/Game.cs', line=12, addresses=['0x80004008'])]
        if same_line:
            data['methods'][0]['breakpoints'][0]['addresses'].append('0x80004004')
            data['methods'][0]['breakpoints'][1]['addresses'] = ['0x80004008']
            data['methods'][0]['breakpoints'].pop()
        data['methods'].append(dict(symbol='callee', nativeStart='0x80005000', nativeSize=256,
            breakpoints=[dict(document='/build/日本/Game.cs', line=20, addresses=['0x80005000']),
                         dict(document='/build/日本/Game.cs', line=21, addresses=['0x80005004'])]))
        data['unwind'][0]['cfaOffset'] = 32
        data['unwind'].append(dict(start='0x80005000', end='0x80005100', supported=True,
            cfaRegister=1, cfaOffset=0, registers={'65': dict(kind='undefined')}))
        self.address.write_text(json.dumps(data))
        self.attach()
        self.target.memory[0x80004000] = opcode.to_bytes(4, 'big') + bytes.fromhex('60000000') * 31
        self.target.step_destinations[0x80004000] = 0x80005000

    def test_step_over_call_runs_to_return_and_removes_temporary_breakpoint(self):
        self.attach_call_target()
        self.bp(lines=(10,))
        self.target.continue_stop = 'T0540:80004004;'
        self.client.request('next', dict(threadId=1))
        stop = self.client.event('stopped')
        self.assertEqual(('step', 'SourceLocation'), (stop['reason'], stop['description']))
        self.assertEqual(1, self.target.commands.count('s'))
        self.assertEqual(1, self.target.commands.count('c'))
        self.assertIn('Z0,80004004,4', self.target.commands)
        self.assertIn('z0,80004004,4', self.target.commands)
        self.assertEqual({0x80004000}, self.target.breakpoints)
        self.assertEqual(11, self.client.request('stackTrace', dict(threadId=1))['body']['stackFrames'][0]['line'])

    def test_step_over_call_keeps_user_breakpoints_inside_callee(self):
        self.attach_call_target()
        entries = self.bp(lines=(10, 21))
        self.target.continue_stop = 'T0540:80005004;'
        self.client.request('next', dict(threadId=1))
        stop = self.client.event('stopped')
        self.assertEqual('breakpoint', stop['reason'])
        self.assertEqual([entries[1]['id']], stop['hitBreakpointIds'])
        self.assertEqual({0x80004000, 0x80005004}, self.target.breakpoints)
        self.assertEqual(21, self.client.request('stackTrace', dict(threadId=1))['body']['stackFrames'][0]['line'])

    def test_step_over_indirect_call_preserves_user_breakpoint_at_return(self):
        self.attach_call_target(opcode=0x4e800421)  # bctrl
        entry = self.bp(lines=(11,))[0]
        self.target.continue_stop = 'T0540:80004004;'
        self.client.request('next', dict(threadId=1))
        stop = self.client.event('stopped')
        self.assertEqual('breakpoint', stop['reason'])
        self.assertEqual([entry['id']], stop['hitBreakpointIds'])
        self.assertEqual({0x80004004}, self.target.breakpoints)
        self.assertEqual(1, self.target.commands.count('c'))

    def test_step_over_conditional_linked_branch_keeps_single_step_fallback(self):
        self.attach_call_target(opcode=0x41821001)  # conditional bcl
        self.target.step_destinations[0x80005000] = 0x80004004
        self.client.request('next', dict(threadId=1))
        self.assertEqual('SourceLocation', self.client.event('stopped')['description'])
        self.assertEqual(2, self.target.commands.count('s'))
        self.assertNotIn('c', self.target.commands)

    def test_step_over_fast_return_on_same_line_keeps_advancing(self):
        self.attach_call_target(same_line=True)
        self.target.continue_stop = 'T0540:80004004;'
        self.client.request('next', dict(threadId=1))
        self.assertEqual('SourceLocation', self.client.event('stopped')['description'])
        self.assertEqual(2, self.target.commands.count('s'))
        self.assertEqual(11, self.client.request('stackTrace', dict(threadId=1))['body']['stackFrames'][0]['line'])

    def test_step_over_recursive_call_proves_activation_before_same_line_return(self):
        self.attach_call_target(opcode=0x48000001, same_line=True)
        self.target.step_destinations[0x80004000] = 0x80004000
        self.target.step_stack_pointers[0x80004000] = 0x816fffe0
        self.target.continue_stack_pointer = 0x81700000
        self.target.continue_stop = 'T0540:80004004;'
        self.client.request('next', dict(threadId=1))
        self.assertEqual('SourceLocation', self.client.event('stopped')['description'])
        self.assertEqual(1, self.target.commands.count('s'))
        self.assertEqual(1, self.target.commands.count('c'))
        self.assertEqual(10, self.client.request('stackTrace', dict(threadId=1))['body']['stackFrames'][0]['line'])

    def test_step_over_callee_entry_breakpoint_prevents_fast_continue(self):
        self.attach_call_target()
        entry = self.bp(lines=(20,))[0]
        self.client.request('next', dict(threadId=1))
        stop = self.client.event('stopped')
        self.assertEqual('breakpoint', stop['reason'])
        self.assertEqual([entry['id']], stop['hitBreakpointIds'])
        self.assertNotIn('c', self.target.commands)
        self.assertNotIn('Z0,80004004,4', self.target.commands)

    def test_pause_during_fast_step_removes_return_breakpoint(self):
        self.attach_call_target()
        self.client.request('next', dict(threadId=1))
        self.assertTrue(self.target.continuing.wait(3))
        self.client.request('pause', dict(threadId=1))
        self.assertEqual('pause', self.client.event('stopped')['reason'])
        self.assertFalse(self.target.breakpoints)
        self.assertIn('z0,80004004,4', self.target.commands)
        self.assertTrue(self.client.request('stackTrace', dict(threadId=1))['body']['stackFrames'])

    def test_pause_while_reading_step_instruction_does_not_inject_rsp_stop(self):
        self.attach_call_target()
        self.target.delay_memory = True
        self.client.request('next', dict(threadId=1))
        self.assertTrue(self.target.memory_requested.wait(3))
        self.client.request('pause', dict(threadId=1))
        self.target.memory_release.set()
        self.assertEqual('pause', self.client.event('stopped')['reason'])
        self.assertEqual(0, self.target.interrupts)
        self.assertNotIn('s', self.target.commands)
        self.assertNotIn('c', self.target.commands)
        self.assertTrue(self.client.request('stackTrace', dict(threadId=1))['body']['stackFrames'])

    def test_disconnect_during_fast_step_removes_return_breakpoint(self):
        self.attach_call_target()
        self.client.request('next', dict(threadId=1))
        self.assertTrue(self.target.continuing.wait(3))
        self.client.request('disconnect')
        self.client.event('terminated')
        self.assertFalse(self.target.breakpoints)
        self.assertNotIn('k', self.target.commands)

    def test_signal_during_fast_step_stays_visible(self):
        self.attach_call_target()
        self.target.continue_stop = 'T0b40:80005004;'
        self.client.request('next', dict(threadId=1))
        stop = self.client.event('stopped')
        self.assertEqual(('exception', 'Signal'), (stop['reason'], stop['description']))
        self.assertFalse(self.target.breakpoints)

    def test_unexpected_trap_during_fast_step_stays_visible(self):
        self.attach_call_target()
        self.target.continue_stop = 'T0540:80005004;'
        self.client.request('next', dict(threadId=1))
        stop = self.client.event('stopped')
        self.assertEqual(('exception', 'Signal'), (stop['reason'], stop['description']))
        self.assertFalse(self.target.breakpoints)

    def test_exit_during_fast_step_does_not_write_to_exited_target(self):
        self.attach_call_target()
        self.target.continue_stop = 'W00'
        self.client.request('next', dict(threadId=1))
        self.client.event('terminated')
        self.assertEqual('c', self.target.commands[-1])

    def test_exit_while_resuming_from_breakpoint_does_not_reinstall_breakpoints(self):
        self.attach()
        self.bp(lines=(10, 11))
        self.client.request('continue', dict(threadId=1))
        self.client.event('stopped')
        self.target.step_reply = 'W00'
        self.client.request('continue', dict(threadId=1))
        self.client.event('terminated')
        self.assertEqual('s', self.target.commands[-1])
        self.assertFalse(self.target.breakpoints)
        self.client.request('threads', success=False)

    def test_stack_snapshot_reuses_frames_and_refreshes_after_execution(self):
        self.attach()
        first = self.client.request('stackTrace', dict(threadId=1))['body']['stackFrames']
        reads = self.target.commands.count('p1')
        again = self.client.request('stackTrace', dict(threadId=1, startFrame=0, levels=1))['body']['stackFrames']
        self.assertEqual(first, again)
        self.assertEqual(reads, self.target.commands.count('p1'))
        self.client.request('stepIn', dict(threadId=1))
        self.client.event('stopped')
        refreshed = self.client.request('stackTrace', dict(threadId=1))['body']['stackFrames']
        self.assertNotEqual(first[0]['id'], refreshed[0]['id'])
        self.assertGreater(self.target.commands.count('p1'), reads)

    def test_partial_stack_diagnostic_is_not_repeated_after_steps(self):
        data = json.loads(self.address.read_text())
        data['unwind'] = []
        self.address.write_text(json.dumps(data))
        self.attach()
        self.client.request('stackTrace', dict(threadId=1))
        self.assertIn('Partial stack: MissingRules', self.client.event('output')['output'])
        self.bp(lines=(11,))
        self.client.request('continue', dict(threadId=1))
        self.client.event('stopped')
        self.client.request('stackTrace', dict(threadId=1))
        self.client.request('threads')  # Drain any output emitted after the stack response.
        self.assertFalse(any('Partial stack' in e.get('body', {}).get('output', '') for e in self.client.events))

    def test_invalid_transport_is_rejected_before_connecting(self):
        for args in [dict(transport="unknown"), dict(transport="usbGecko", serialPort="", baudRate=115200),
                     dict(transport="usbGecko", serialPort="COM5", baudRate=0),
                     dict(transport="tcp", host="localhost", port=0), dict(timeoutSeconds=121)]:
            with self.subTest(args=args):
                self.client.request("attach", dict(elf=str(self.elf), sourceMap=str(self.source),
                    addressMap=str(self.address), **args), success=False)

    def test_resume_reports_missing_rejected_and_verified_breakpoints(self):
        self.attach()
        self.client.request('continue', dict(threadId=1))
        self.assertIn('no verified source breakpoints', self.client.event('output')['output'])
        self.client.request('pause', dict(threadId=1))
        self.client.event('stopped')
        self.assertFalse(self.bp(lines=(999,))[0]['verified'])
        rejected = self.client.event('output')
        self.assertEqual('stderr', rejected['category'])
        self.assertIn('/build/日本/Game.cs:999', rejected['output'])
        self.assertIn('No native address', rejected['output'])
        self.assertTrue(self.bp(lines=(11,))[0]['verified'])
        self.client.request('continue', dict(threadId=1))
        self.assertIn('1 verified source breakpoint(s) at 1 native address(es)', self.client.event('output')['output'])
        self.assertEqual('breakpoint', self.client.event('stopped')['reason'])

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

    def test_generated_variable_ranges_survive_interior_stops_and_location_changes(self):
        spec = importlib.util.spec_from_file_location('addresses', Path(__file__).resolve().parents[1] / 'scripts/dnd_debug_addresses.py')
        addresses = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(addresses)
        symbol = 'dnd_value_Program_Main_test'
        source = dict(schemaVersion=2, methods=[dict(symbol=symbol, assembly='Demo', methodToken=1,
            declaringType='Program', name='Main', sourceLocations=[
                dict(document='/build/日本/Game.cs', startLine=11, ilOffset=1),
                dict(document='/build/日本/Game.cs', startLine=12, ilOffset=3)], variables=[
                dict(nativeName='l0', name='local', type='System.Int32', group='Locals', storageBytes=4,
                     scopeStart=0, scopeEnd=2)])])
        lines = '''/build/日本/Game.cs:
Game.cs 11 0x80004008 x
generated_program.c 1 0x80004018 x
Game.cs 12 0x80004020 x
Game.cs - 0x80004028
'''
        mapped = addresses.resolve(source, lines, f'1: 80004000 256 FUNC GLOBAL DEFAULT 2 {symbol}\n')
        info = f''' <1><10>: Abbrev Number: 3 (DW_TAG_subprogram)
    <11> DW_AT_name : {symbol}
    <12> DW_AT_low_pc : 0x80004000
    <13> DW_AT_frame_base : 1 byte block: 9c (DW_OP_call_frame_cfa)
 <2><20>: Abbrev Number: 5 (DW_TAG_variable)
    <21> DW_AT_name : l0
    <22> DW_AT_location : 0x10 (location list)
'''
        locs = '''00000010 80004008 80004010 (DW_OP_fbreg: 8)
00000018 80004010 80004018 (DW_OP_fbreg: 4)
00000020 <End of list>
'''
        addresses.attach_variables(mapped, source, info, locs)
        self.source.write_text(json.dumps(source))
        previous = json.loads(self.address.read_text())
        mapped.update(elfSha256=previous['elfSha256'],
            sourceMapSha256=hashlib.sha256(self.source.read_bytes()).hexdigest(), unwind=previous['unwind'])
        self.address.write_text(json.dumps(mapped))
        target = Target()
        target.pc = 0x8000400c  # Inside a row, without an exact source/breakpoint address.
        self.attach(target=target)
        old_ref = None
        for pc, expected in [(0x8000400c, '-1'), (0x80004010, '3'), (0x80004014, '3'),
                             (0x80004018, None), (0x80004020, None), (0x80004028, None)]:
            with self.subTest(pc=hex(pc)):
                if target.pc != pc:
                    target.continue_stop = f'T0540:{pc:08x};'
                    self.client.request('continue', dict(threadId=1))
                    self.client.event('stopped')
                    if old_ref is not None:
                        self.client.request('variables', dict(variablesReference=old_ref), success=False)
                frame = self.client.request('stackTrace', dict(threadId=1))['body']['stackFrames'][0]
                scopes = self.client.request('scopes', dict(frameId=frame['id']))['body']['scopes']
                if expected is None:
                    self.assertEqual([], scopes)
                else:
                    self.assertEqual('Locals', scopes[0]['name'])
                    old_ref = scopes[0]['variablesReference']
                    values = self.client.request('variables', dict(variablesReference=old_ref))['body']['variables']
                    self.assertEqual([('local', expected)], [(v['name'], v['value']) for v in values])

    def test_invalid_variable_ranges_are_rejected(self):
        base = json.loads(self.address.read_text())
        for start, end in [('0x80004010', '0x80004010'), ('0x80004010', '0x8000400c'),
                           ('0x80003ffc', '0x80004008'), ('0x80004008', '0x80004104'),
                           ('0x80004009', '0x80004010')]:
            with self.subTest(start=start, end=end):
                data = dict(base)
                data['methods'][0]['variables'] = [dict(name='local', type='System.Int32', group='Locals',
                    storageBytes=4, location=dict(kind='cfaOffset', offset=8), visibleAddresses=[],
                    visibleRanges=[dict(start=start, end=end)])]
                self.address.write_text(json.dumps(data))
                response = self.client.request('attach', dict(host='127.0.0.1', port=1, elf=str(self.elf),
                    sourceMap=str(self.source), addressMap=str(self.address)), success=False)
                self.assertIn('Invalid PowerPC metadata address' if start.endswith('09') else
                              'Invalid variable visibility extent', response['message'])

    def test_caller_variable_ranges_use_call_pc_and_caller_cfa(self):
        data = json.loads(self.address.read_text())
        data['methods'][0]['variables'] = [dict(name='local', type='System.Int32', group='Locals',
            storageBytes=4, location=dict(kind='cfaOffset', offset=8), visibleAddresses=['0x80004008'],
            visibleRanges=[dict(start='0x80004008', end='0x80004010')])]
        data['methods'].append(dict(symbol='callee', nativeStart='0x80005000', nativeSize=256, breakpoints=[]))
        data['unwind'].append(dict(start='0x80005000', end='0x80005100', supported=True,
            cfaRegister=1, cfaOffset=32, registers={'65': dict(kind='memory', offset=-4)}))
        self.address.write_text(json.dumps(data))
        target = Target()
        target.pc = 0x80005000
        target.memory[0x8170001c] = bytes.fromhex('80004010')  # Call PC is 0x8000400c, inside the range.
        target.memory[0x81700028] = bytes.fromhex('00000007')
        self.attach(target=target)
        frames = self.client.request('stackTrace', dict(threadId=1))['body']['stackFrames']
        self.assertEqual(2, len(frames))
        self.assertEqual('0x80004010', frames[1]['instructionPointerReference'])
        scopes = self.client.request('scopes', dict(frameId=frames[1]['id']))['body']['scopes']
        values = self.client.request('variables', dict(variablesReference=scopes[0]['variablesReference']))['body']['variables']
        self.assertEqual([('local', '7')], [(v['name'], v['value']) for v in values])

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

    def test_invalid_source_mapping_request_does_not_terminate_healthy_target(self):
        self.target = Target()
        self.client.request('attach', dict(host='127.0.0.1', port=self.target.port,
            elf=str(self.elf), sourceMap=str(self.source), addressMap=str(self.address),
            sourceFileMap={'/build/one':'/local', '/build/two':'/local'}))
        self.client.event('initialized')
        result = self.client.request('setBreakpoints', dict(source=dict(path='/local/Game.cs'),
            breakpoints=[dict(line=10)]), success=False)
        self.assertIn('Ambiguous source path mapping', result['message'])
        self.client.request('threads')
        self.assertFalse(any(message.get('event')=='terminated' for message in self.client.events))

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
