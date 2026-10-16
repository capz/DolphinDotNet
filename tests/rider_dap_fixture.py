#!/usr/bin/env python3
"""Fake RSP peer for the real packaged Java -> .NET DAP acceptance path."""
import json
from pathlib import Path
import sys
from test_debug_adapter import Target, SerialRelay, write_symbol_fixture

root = Path(sys.argv[1])
root.mkdir(parents=True, exist_ok=True)
elf, source, addresses = write_symbol_fixture(root)
target = Target()
relay = SerialRelay(target.port) if sys.argv[2] == 'usbGecko' else None
args = dict(elf=str(elf), sourceMap=str(source), addressMap=str(addresses),
            sourceFileMap={'/build/日本': str(root / '日本')}, timeoutSeconds=3)
if relay:
    args.update(transport='usbGecko', serialPort=relay.path, baudRate=115200)
else:
    args.update(transport='tcp', host='127.0.0.1', port=target.port)
print(json.dumps(args), flush=True)
try:
    for line in sys.stdin:
        if line.strip() == 'status':
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
