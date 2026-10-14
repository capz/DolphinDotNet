"""Local deterministic DNS/HTTP endpoints for compiled networking smoke tests."""
import socket
import struct
import threading
import subprocess
import sys

ready = [threading.Event() for _ in range(3)]

def dns_answer(query):
    question = query[12:]
    return query[:2] + b'\x81\x80\x00\x01\x00\x01\x00\x00\x00\x00' + question + b'\xc0\x0c\x00\x01\x00\x01' + struct.pack('!IH', 30, 4) + socket.inet_aton('127.0.0.1')

def dns_udp():
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.bind(('127.0.0.1', 15353))
    ready[0].set()
    while True:
        query, peer = sock.recvfrom(4096)
        if b'\x03tcp\x04test' in query:
            sock.sendto(query[:2] + b'\x83\x80\x00\x01\x00\x00\x00\x00\x00\x00' + query[12:], peer)
        else:
            sock.sendto(dns_answer(query), peer)

def exact(sock, count):
    out = bytearray()
    while len(out) < count:
        chunk = sock.recv(count - len(out))
        if not chunk:
            raise EOFError
        out.extend(chunk)
    return bytes(out)

def dns_tcp():
    sock = socket.socket()
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    sock.bind(('127.0.0.1', 15353))
    sock.listen(4)
    ready[1].set()
    while True:
        peer, _ = sock.accept()
        with peer:
            query = exact(peer, struct.unpack('!H', exact(peer, 2))[0])
            answer = dns_answer(query)
            peer.sendall(struct.pack('!H', len(answer)) + answer)

def http_peer(peer):
    with peer:
        request = bytearray()
        while b'\r\n\r\n' not in request and len(request) < 8192:
            part = peer.recv(1024)
            if not part:
                return
            request.extend(part)
        path = request.split(b' ')[1]
        body = b'{"value":42,"name":"GameCube","ok":true}'
        if path == b'/large':
            body = bytes(i % 251 for i in range(12001))
            peer.sendall(b'HTTP/1.1 200 OK\r\nContent-Length: ' + str(len(body)).encode() + b'\r\n\r\n' + body)
        elif path == b'/chunked':
            peer.sendall(b'HTTP/1.1 100 Continue\r\n\r\nHTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n')
            for part in (body[:5], body[5:]):
                peer.sendall(f'{len(part):x}\r\n'.encode() + part + b'\r\n')
            peer.sendall(b'0\r\nX-Trailer: yes\r\n\r\n')
        elif path == b'/truncated':
            peer.sendall(b'HTTP/1.1 200 OK\r\nContent-Length: 50\r\n\r\nshort')
        elif path == b'/ambiguous':
            peer.sendall(b'HTTP/1.1 200 OK\r\nContent-Length: 4\r\nTransfer-Encoding: chunked\r\n\r\n0\r\n\r\n')
        else:
            peer.sendall(b'HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: ' + str(len(body)).encode() + b'\r\n\r\n' + body)

def http():
    sock = socket.socket()
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    sock.bind(('127.0.0.1', 18081))
    sock.listen(4)
    ready[2].set()
    while True:
        peer, _ = sock.accept()
        threading.Thread(target=http_peer, args=(peer,), daemon=True).start()

for service in (dns_udp, dns_tcp, http):
    threading.Thread(target=service, daemon=True).start()
print('network fixtures ready', flush=True)
if len(sys.argv) > 1:
    for event in ready:
        if not event.wait(5):
            raise RuntimeError('fixture failed to start')
    sys.exit(subprocess.run(sys.argv[1:], timeout=45).returncode)
threading.Event().wait()
