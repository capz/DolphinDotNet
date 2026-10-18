#!/usr/bin/env python3
"""Run the actual AOT KartNetwork against a UDP peer, including loss/rejoin."""
import os, socket, struct, subprocess, sys, time

def main():
    with socket.socket(socket.AF_INET,socket.SOCK_DGRAM) as peer:
        peer.bind(('127.0.0.1',28779));peer.settimeout(.1)
        process=subprocess.Popen(sys.argv[1:],env=dict(os.environ,DND_ASYNC_MANUAL='0'))
        first=None;sequence=0;received=0;hit_ack=False;local_item=0;hazard=None
        try:
            deadline=time.monotonic()+40
            while process.poll() is None and time.monotonic()<deadline:
                try:data,endpoint=peer.recvfrom(1200)
                except socket.timeout:continue
                assert len(data)==252 and struct.unpack('!iii',data[:12])==(0x4b415232,701,11),data
                hit_ack=hit_ack or struct.unpack_from('!i',data,88)[0]==9001
                local_item=struct.unpack_from('!i',data,60)[0] or local_item
                if hazard is None:hazard=struct.unpack_from('!ii',data,16)
                received+=1;now=struct.unpack_from("!i",data,40)[0]*.001
                if first is None:first=now
                # Use transmitted simulation time so a loaded CI runner cannot
                # shorten the loss interval relative to the game's timeout.
                # One second online, four seconds of silence, then reconnect.
                if 1<now-first<5:continue
                sequence+=1
                values=[0x4b415232,701,22,sequence,50000,0,0,0,10000,1,1000,11,0,0,0]+[0]*48
                values[15:23]=[9001,2,hazard[0],hazard[1],0,0,14000,local_item]
                peer.sendto(struct.pack('!63i',*values),endpoint)
            if process.poll() is None:raise TimeoutError('Kart network fixture hung')
            if process.returncode:raise RuntimeError(f'Kart network fixture failed: {process.returncode}')
            assert received>20 and hit_ack and local_item>0,(received,hit_ack,local_item)
            print(f'AOT KartNetwork bidirectional UDP, item hit/ACK, offline continuation and rejoin passed ({received} snapshots).')
        finally:
            if process.poll() is None:process.kill();process.wait()
if __name__=='__main__':main()
