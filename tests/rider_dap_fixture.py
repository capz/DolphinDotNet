#!/usr/bin/env python3
"""Fake RSP peer for the real packaged Java -> .NET DAP acceptance path."""
import json
from pathlib import Path
import sys
import socket
import struct
from test_debug_adapter import Target, SerialRelay, write_symbol_fixture, write_inspection_fixture

root = Path(sys.argv[1])
root.mkdir(parents=True, exist_ok=True)
elf, source, addresses = write_symbol_fixture(root)
target = Target()
target.memory.update(write_inspection_fixture(addresses))
metadata = json.loads(addresses.read_text())
metadata['debuggerTelemetry'] = dict(version=1, size=8, address='0x80009000')
metadata['debuggerHeartbeat'] = dict(version=1, size=24, supported=True, address='0x80009100')
addresses.write_text(json.dumps(metadata))
target.memory[0x80009000] = bytes.fromhex('444e443100000000')
target.memory[0x80009100] = bytes.fromhex('444e4842') + bytes(20)
relay = SerialRelay(target.port) if sys.argv[2] == 'usbGecko' else None
args = dict(elf=str(elf), sourceMap=str(source), addressMap=str(addresses),
            sourceFileMap={'/build/日本': str(root / '日本')}, timeoutSeconds=3)
args.update(heartbeatHost='127.0.0.1', heartbeatPort=0)
if relay:
    args.update(transport='usbGecko', serialPort=relay.path, baudRate=115200)
else:
    args.update(transport='tcp', host='127.0.0.1', port=target.port)
print(json.dumps(args), flush=True)
try:
    for line in sys.stdin:
        if line.strip() == 'heartbeat':
            magic, host, port, high, low, enabled = struct.unpack('>6I', target.memory[0x80009100])
            live = False
            if enabled and port:
                ping = struct.pack('>5I', magic, 1, high, low, 1)
                with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as udp:
                    udp.settimeout(.3)
                    for _ in range(2):
                        try:
                            udp.sendto(ping, ('127.0.0.1', port))
                            live = udp.recvfrom(64)[0] == ping[:4] + struct.pack('>I', 2) + ping[8:]
                            break
                        except OSError:
                            pass
            print(json.dumps(dict(live=live)), flush=True)
        elif line.strip() == 'status':
            print(json.dumps(dict(breakpoints=sorted(target.breakpoints), errors=[str(e) for e in target.errors],
                                  commands=target.commands)), flush=True)
        else:
            break
finally:
    if relay:
        relay.close()
    target.thread.join(timeout=2)
    if target.errors:
        raise RuntimeError(target.errors)
