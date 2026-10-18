#!/usr/bin/env python3
"""Run the actual AOT KartNetwork against a UDP peer, including loss/rejoin."""
import os, socket, struct, subprocess, sys, time

def main():
    with socket.socket(socket.AF_INET,socket.SOCK_DGRAM) as peer:
        peer.bind(('127.0.0.1',28779));peer.settimeout(.1)
        process=subprocess.Popen(sys.argv[1:],env=dict(os.environ,DND_ASYNC_MANUAL='0'))
        first=None;sequence=0;received=0
        try:
            deadline=time.monotonic()+40
            while process.poll() is None and time.monotonic()<deadline:
                try:data,endpoint=peer.recvfrom(1200)
                except socket.timeout:continue
                assert len(data)==48 and struct.unpack('!iii',data[:12])==(0x4b415231,701,11),data
                received+=1;now=struct.unpack_from("!i",data,40)[0]*.001
                if first is None:first=now
                # Use transmitted simulation time so a loaded CI runner cannot
                # shorten the loss interval relative to the game's timeout.
                # One second online, four seconds of silence, then reconnect.
                if 1<now-first<5:continue
                sequence+=1
                peer.sendto(struct.pack('!12i',0x4b415231,701,22,sequence,50000,0,0,0,10000,1,1000,11),endpoint)
            if process.poll() is None:raise TimeoutError('Kart network fixture hung')
            if process.returncode:raise RuntimeError(f'Kart network fixture failed: {process.returncode}')
            assert received>20,received
            print(f'AOT KartNetwork bidirectional UDP, offline continuation and rejoin passed ({received} snapshots).')
        finally:
            if process.poll() is None:process.kill();process.wait()
if __name__=='__main__':main()
