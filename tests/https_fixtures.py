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

def certificates(root,include_intermediate=False):
    def run(*args):subprocess.run(['openssl',*args],cwd=root,check=True,stdout=subprocess.DEVNULL,stderr=subprocess.PIPE)
    run('req','-x509','-newkey','rsa:2048','-nodes','-keyout','ca.key','-out','ca.pem','-days','5','-subj','/CN=DND Test Root')
    run('req','-newkey','rsa:2048','-nodes','-keyout','server.key','-out','server.csr','-subj','/CN=localhost')
    (root/'extensions').write_text('subjectAltName=DNS:localhost\nbasicConstraints=CA:FALSE\nkeyUsage=digitalSignature,keyEncipherment\nextendedKeyUsage=serverAuth\n')
    run('x509','-req','-in','server.csr','-CA','ca.pem','-CAkey','ca.key','-CAcreateserial','-out','server.pem','-days','4','-extfile','extensions')
    if include_intermediate:
        run('req','-newkey','rsa:2048','-nodes','-keyout','intermediate.key','-out','intermediate.csr','-subj','/CN=DND Short Intermediate')
        (root/'intermediate-extensions').write_text('basicConstraints=critical,CA:TRUE,pathlen:0\nkeyUsage=critical,keyCertSign,cRLSign\n')
        run('x509','-req','-in','intermediate.csr','-CA','ca.pem','-CAkey','ca.key','-CAcreateserial','-out','intermediate.pem','-days','1','-extfile','intermediate-extensions')
        run('x509','-req','-in','server.csr','-CA','intermediate.pem','-CAkey','intermediate.key','-CAcreateserial','-out','intermediate-leaf.pem','-days','4','-extfile','extensions')
        (root/'intermediate-chain.pem').write_bytes((root/'intermediate-leaf.pem').read_bytes()+(root/'intermediate.pem').read_bytes())
    run('req','-x509','-newkey','rsa:2048','-nodes','-keyout','other.key','-out','other.pem','-days','2','-subj','/CN=Untrusted Test Root')
    (root/'entropy.bin').write_bytes(os.urandom(48))

def attempt(root,command,roots,hostname,utc,success,local=False,chain='server.pem'):
    context=ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER);context.minimum_version=ssl.TLSVersion.TLSv1_2;context.maximum_version=ssl.TLSVersion.TLSv1_2
    context.load_cert_chain(root/chain,root/'server.key')
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
        subprocess.run([*command,str(root/roots),str(root/'entropy.bin'),str(listener.getsockname()[1]),hostname,str(utc),'1' if success else '0',*(['local'] if local else [])],check=True,timeout=20)
    finally:listener.close();worker.join(timeout=1)

def main(command):
    with tempfile.TemporaryDirectory(prefix='dnd-https-') as folder:
        root=Path(folder);certificates(root,include_intermediate=True);now=int(time.time())
        for ca,host,utc,success in [('ca.pem','localhost',now,True),('other.pem','localhost',now,False),('ca.pem','wrong.example',now,False),('ca.pem','localhost',1704067200,False),('ca.pem','localhost',2114380800,False)]:
            attempt(root,command,ca,host,utc,success)
        # Checking local calendar without a timezone must be stricter than the
        # exact center: newly issued and almost-expired leaves fail unless the
        # whole possible UTC interval is covered. Trust/name failures stay set.
        for ca,host,local,success in [('ca.pem','localhost',now+2*86400,True),('other.pem','localhost',now+2*86400,False),('ca.pem','wrong.example',now+2*86400,False),('ca.pem','localhost',now+3600,False),('ca.pem','localhost',now+4*86400-3600,False)]:
            attempt(root,command,ca,host,local,success,local=True)
        attempt(root,command,'ca.pem','localhost',now+4*86400-3600,True)
        # Leaf and root cover the entire interval, but this intermediate only
        # covers its exact center. The local-clock callback must visit it too.
        attempt(root,command,'ca.pem','localhost',now+20*3600,True,chain='intermediate-chain.pem')
        attempt(root,command,'ca.pem','localhost',now+20*3600,False,local=True,chain='intermediate-chain.pem')
        print('HTTPS: exact/local clocks, full timezone uncertainty interval, intermediate validity, fragmented verified response, untrusted root, hostname mismatch, not-yet-valid and expired certificates passed.')

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('command',nargs='+');main(p.parse_args().command)
