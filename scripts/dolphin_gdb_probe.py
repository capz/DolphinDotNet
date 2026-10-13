"""Strict PowerPC entry-point verification through Dolphin's GDB remote stub.

Dolphin waits for the debugger before executing the guest when GDBPort is set.
Install breakpoints at ELF symbol addresses and require actual guest PC stops.
A loader log, a live emulator process, or a successful debugger connection alone
is never sufficient to mark an entry point as reached.
"""
from dataclasses import dataclass
import fnmatch
from pathlib import Path
import re
import socket
import subprocess
import time


@dataclass(frozen=True)
class ProbeResult:
    success: bool
    message: str


def resolve_symbols(elf: Path, patterns: list[str]) -> list[tuple[str, int]]:
    if not elf.is_file():
        raise ValueError(f"Missing ELF with symbols: {elf}")
    output = subprocess.run(["readelf", "-sW", str(elf)], check=True,
                            capture_output=True, text=True).stdout
    symbols = []
    for line in output.splitlines():
        parts = line.split()
        if len(parts) < 8 or not parts[0].endswith(":") or parts[3] != "FUNC":
            continue
        try:
            address = int(parts[1], 16)
        except ValueError:
            continue
        if address:
            symbols.append((parts[7], address))
    selected = []
    for pattern in patterns:
        matches = sorted(set((name, address) for name, address in symbols
                             if fnmatch.fnmatchcase(name, pattern)))
        if len(matches) != 1:
            raise ValueError(f"Expected exactly one ELF function for {pattern!r}, "
                             f"found {len(matches)}: {matches[:5]}")
        selected.append(matches[0])
    addresses = [address for _, address in selected]
    if len(addresses) != len(set(addresses)):
        raise ValueError("Expected entry points resolve to the same address; cannot prove separate calls")
    return selected


def parse_stop_pc(packet: str) -> int:
    match = re.fullmatch(r"T[0-9a-fA-F]{2}40:([0-9a-fA-F]{8});.*", packet)
    if not match:
        raise ValueError(f"Missing PowerPC PC in GDB stop reply: {packet!r}")
    return int(match.group(1), 16)


def _send(conn: socket.socket, command: str) -> None:
    payload = command.encode("ascii")
    conn.sendall(b"$" + payload + b"#" + f"{sum(payload) & 255:02x}".encode("ascii"))


def _reply(conn: socket.socket, deadline: float) -> str:
    while True:
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            raise TimeoutError("GDB reply deadline exceeded")
        conn.settimeout(remaining)
        byte = conn.recv(1)
        if not byte:
            raise ConnectionError("Dolphin closed GDB connection")
        if byte == b"$":
            break
        if byte not in (b"+", b"-"):
            raise ValueError(f"Unexpected GDB framing byte: {byte!r}")
    payload = bytearray()
    while True:
        conn.settimeout(max(0.01, deadline - time.monotonic()))
        byte = conn.recv(1)
        if not byte:
            raise ConnectionError("Truncated GDB response")
        if byte == b"#":
            break
        if len(payload) >= 8192:
            raise ValueError("Oversized GDB response")
        payload.extend(byte)
    checksum = conn.recv(2)
    if len(checksum) != 2 or int(checksum, 16) != (sum(payload) & 255):
        raise ValueError("Invalid GDB response checksum")
    conn.sendall(b"+")
    return payload.decode("ascii")


def probe(port: int, expected: list[tuple[str, int]], seconds: float = 15) -> ProbeResult:
    deadline = time.monotonic() + seconds
    lines = []
    try:
        conn = None
        while time.monotonic() < deadline:
            try:
                conn = socket.create_connection(("127.0.0.1", port), timeout=0.4)
                break
            except OSError:
                time.sleep(0.15)
        if conn is None:
            raise TimeoutError("Dolphin GDB stub never opened a connection")
        with conn:
            # Dolphin holds the guest at startup while the GDB stub accepts
            # its first connection. Install breakpoints before resuming.
            for name, address in expected:
                _send(conn, f"Z0,{address:08x},4")
                reply = _reply(conn, deadline)
                if reply != "OK":
                    raise ValueError(f"Cannot set breakpoint {name} at 0x{address:08x}: {reply!r}")
            for name, address in expected:
                _send(conn, "c")
                try:
                    packet = _reply(conn, deadline - 3)
                except TimeoutError:
                    # Capture the guest PC instead of guessing whether startup
                    # or a debugger breakpoint caused the missing entry hit.
                    conn.sendall(bytes([3]))
                    try:
                        stopped = _reply(conn, time.monotonic() + 2)
                        lines.append(f"DND_GDB_TIMEOUT_PC=0x{parse_stop_pc(stopped):08x}")
                    except (OSError, ValueError, TimeoutError) as diagnostic_error:
                        lines.append(f"DND_GDB_TIMEOUT_PC_UNAVAILABLE: {diagnostic_error}")
                    raise TimeoutError(f"Guest never reached {name} at 0x{address:08x}")
                actual = parse_stop_pc(packet)
                if actual != address:
                    raise ValueError(f"Guest stopped at 0x{actual:08x}, expected {name} at 0x{address:08x}")
                lines.append(f"DND_GDB_ENTRY_REACHED {name} PC=0x{actual:08x}")
                _send(conn, f"z0,{address:08x},4")
                if _reply(conn, deadline) != "OK":
                    raise ValueError(f"Cannot clear breakpoint {name}")
            _send(conn, "c")
        return ProbeResult(True, "\n".join(lines))
    except (OSError, ValueError, TimeoutError) as error:
        return ProbeResult(False, "\n".join(lines + [f"DND_GDB_ENTRY_FAILED: {error}"]))
