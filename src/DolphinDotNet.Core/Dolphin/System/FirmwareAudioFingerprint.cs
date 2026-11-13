using System;
using System.Threading;

namespace Dolphin.System;

// Small SHA-256 implementation for the target AOT subset. Input is bounded by
// callers; no platform cryptography service or ROM-sized copy is required.
internal static class FirmwareAudioFingerprint
{
    private static readonly uint[] K={
        0x428a2f98,0x71374491,0xb5c0fbcf,0xe9b5dba5,0x3956c25b,0x59f111f1,0x923f82a4,0xab1c5ed5,
        0xd807aa98,0x12835b01,0x243185be,0x550c7dc3,0x72be5d74,0x80deb1fe,0x9bdc06a7,0xc19bf174,
        0xe49b69c1,0xefbe4786,0x0fc19dc6,0x240ca1cc,0x2de92c6f,0x4a7484aa,0x5cb0a9dc,0x76f988da,
        0x983e5152,0xa831c66d,0xb00327c8,0xbf597fc7,0xc6e00bf3,0xd5a79147,0x06ca6351,0x14292967,
        0x27b70a85,0x2e1b2138,0x4d2c6dfc,0x53380d13,0x650a7354,0x766a0abb,0x81c2c92e,0x92722c85,
        0xa2bfe8a1,0xa81a664b,0xc24b8b70,0xc76c51a3,0xd192e819,0xd6990624,0xf40e3585,0x106aa070,
        0x19a4c116,0x1e376c08,0x2748774c,0x34b0bcb5,0x391c0cb3,0x4ed8aa4a,0x5b9cca4f,0x682e6ff3,
        0x748f82ee,0x78a5636f,0x84c87814,0x8cc70208,0x90befffa,0xa4506ceb,0xbef9a3f7,0xc67178f2};
    private static uint R(uint x,int n)=>(x>>n)|(x<<(32-n));
    internal static string Hash(byte[] data,int offset,int count,CancellationToken cancellation=default)
    {
        if(offset<0||count<0||offset>data.Length-count||count>65536)throw new ArgumentOutOfRangeException(nameof(count));
        var h=new uint[]{0x6a09e667,0xbb67ae85,0x3c6ef372,0xa54ff53a,0x510e527f,0x9b05688c,0x1f83d9ab,0x5be0cd19};
        var w=new uint[64];int blocks=(count+72)/64;
        for(int block=0;block<blocks;block++)
        {
            cancellation.ThrowIfCancellationRequested();
            for(int i=0;i<16;i++)
            {
                uint v=0;for(int j=0;j<4;j++)
                {int at=block*64+i*4+j;int b=at<count?data[offset+at]:at==count?128:at>=blocks*64-4?(int)((uint)(count*8)>>((blocks*64-1-at)*8))&255:0;v=(v<<8)|(uint)b;}
                w[i]=v;
            }
            for(int i=16;i<64;i++)w[i]=w[i-16]+(R(w[i-15],7)^R(w[i-15],18)^(w[i-15]>>3))+w[i-7]+(R(w[i-2],17)^R(w[i-2],19)^(w[i-2]>>10));
            uint a=h[0],b0=h[1],c=h[2],d=h[3],e=h[4],f=h[5],g=h[6],h0=h[7];
            for(int i=0;i<64;i++)
            {uint t=h0+(R(e,6)^R(e,11)^R(e,25))+((e&f)^(~e&g))+K[i]+w[i],u=(R(a,2)^R(a,13)^R(a,22))+((a&b0)^(a&c)^(b0&c));h0=g;g=f;f=e;e=d+t;d=c;c=b0;b0=a;a=t+u;}
            h[0]+=a;h[1]+=b0;h[2]+=c;h[3]+=d;h[4]+=e;h[5]+=f;h[6]+=g;h[7]+=h0;
        }
        const string hex="0123456789abcdef";var result=new char[64];for(int i=0;i<64;i++)result[i]=hex[(int)((h[i/8]>>((7-i%8)*4))&15)];return new string(result);
    }
}
