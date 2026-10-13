"""Capture Dolphin's emulated PowerPC PC/LR through its native GDB stub.

Diagnostic only: inability to connect must never be counted as a successful boot.
"""
import socket
import time

def _packet(connection, command):
    payload = command.encode("ascii")
    checksum = sum(payload) & 0xff
    connection.sendall(b"$" + payload + b"#" + f"{checksum:02x}".encode("ascii"))

def _reply(connection):
    payload = bytearray()
    # Handle GDB acknowledgements and packet framing.
    for _ in range(8192):
        byte = connection.recv(1)
        if not byte:
            raise ConnectionError("GDB stub closed connection")
        if byte == b"$":
            break
    else:
        raise ConnectionError("No GDB response packet")
    for _ in range(8192):
        byte = connection.recv(1)
        if byte == b"#":
            connection.recv(2)
            connection.sendall(b"+")
            return payload.decode("ascii", errors="replace")
        if not byte:
            raise ConnectionError("Truncated GDB packet")
        payload.extend(byte)
    raise ConnectionError("Oversized GDB packet")

def probe(port, seconds=8):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        try:
            with socket.create_connection(("127.0.0.1", port), timeout=0.75) as conn:
                conn.settimeout(2.5)
                conn.sendall(b"\x03")
                stopped = _reply(conn)
                _packet(conn, "p40")  # register 64 = PC in Dolphin's GDBStub
                pc = _reply(conn)
                _packet(conn, "p43")  # register 67 = LR
                lr = _reply(conn)
                _packet(conn, "c")
                return f"DND_GDB_GUEST_STATE stop={stopped} PC=0x{pc} LR=0x{lr}"
        except (ConnectionError, TimeoutError, OSError):
            time.sleep(0.25)
    return "DND_GDB_GUEST_STATE unavailable (stub not listening or probe timed out)"
