"""Hermetic certificate and fragmented TLS acceptance against a native/AOT client."""
import argparse
import os
from pathlib import Path
import socket
import ssl
import subprocess
import tempfile
import threading
import time

def certificates(root):
    def run(*args):subprocess.run(['openssl',*args],cwd=root,check=True,stdout=subprocess.DEVNULL,stderr=subprocess.PIPE)
    run('req','-x509','-newkey','rsa:2048','-nodes','-keyout','ca.key','-out','ca.pem','-days','2','-subj','/CN=DND Test Root')
    run('req','-newkey','rsa:2048','-nodes','-keyout','server.key','-out','server.csr','-subj','/CN=localhost')
    (root/'extensions').write_text('subjectAltName=DNS:localhost\nbasicConstraints=CA:FALSE\nkeyUsage=digitalSignature,keyEncipherment\nextendedKeyUsage=serverAuth\n')
    run('x509','-req','-in','server.csr','-CA','ca.pem','-CAkey','ca.key','-CAcreateserial','-out','server.pem','-days','1','-extfile','extensions')
    run('req','-x509','-newkey','rsa:2048','-nodes','-keyout','other.key','-out','other.pem','-days','2','-subj','/CN=Untrusted Test Root')
    (root/'entropy.bin').write_bytes(os.urandom(48))

def attempt(root,command,roots,hostname,utc,success):
    context=ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER);context.minimum_version=ssl.TLSVersion.TLSv1_2;context.maximum_version=ssl.TLSVersion.TLSv1_2
    context.load_cert_chain(root/'server.pem',root/'server.key')
    listener=socket.socket();listener.bind(('127.0.0.1',0));listener.listen();listener.settimeout(15)
    def serve():
        try:
            conn,_=listener.accept();conn.settimeout(10)
            with context.wrap_socket(conn,server_side=True) as stream:
                request=b''
                while b'\r\n\r\n' not in request:
                    part=stream.recv(4096)
                    if not part:return
                    request+=part
                for byte in b'HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nOK':stream.sendall(bytes([byte]))
        except (ssl.SSLError,ConnectionError,TimeoutError,OSError):pass
    worker=threading.Thread(target=serve,daemon=True);worker.start()
    try:
        subprocess.run([*command,str(root/roots),str(root/'entropy.bin'),str(listener.getsockname()[1]),hostname,str(utc),'1' if success else '0'],check=True,timeout=20)
    finally:listener.close();worker.join(timeout=1)

def main(command):
    with tempfile.TemporaryDirectory(prefix='dnd-https-') as folder:
        root=Path(folder);certificates(root);now=int(time.time())
        for ca,host,utc,success in [('ca.pem','localhost',now,True),('other.pem','localhost',now,False),('ca.pem','wrong.example',now,False),('ca.pem','localhost',1704067200,False),('ca.pem','localhost',2114380800,False)]:
            attempt(root,command,ca,host,utc,success)
        print('HTTPS: fragmented verified response, untrusted root, hostname mismatch, not-yet-valid and expired certificates passed.')

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('command',nargs='+');main(p.parse_args().command)
