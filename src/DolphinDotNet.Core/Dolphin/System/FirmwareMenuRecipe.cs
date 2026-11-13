using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using static Dolphin.Formats.GameCubeBinary;

namespace Dolphin.System;

// This is a finite compiler for the three fingerprinted IPL effects, not a
// public BMS player. All addresses are relative to validated resource extents.
internal sealed class FirmwareMenuRecipe
{
    internal readonly byte[] Data;
    internal readonly int Frames;
    private readonly byte[]? resampling;
    private FirmwareMenuRecipe(byte[] data,int frames,byte[]? coefficients){Data=data;Frames=frames;resampling=coefficients;}
    internal static FirmwareMenuRecipe Compile(byte[] seq,int entry,byte[] bank,byte[]? reverb=null,byte[]? resampling=null)
    {
        var compiler=new Builder(seq,bank);compiler.Track(entry,0);
        return compiler.Finish(reverb,resampling);
    }
    private sealed class Builder
    {
        private readonly byte[] seq,bank;
        private readonly List<int[]> tracks=new List<int[]>(),notes=new List<int[]>(),curves=new List<int[]>();
        private readonly List<int> sends=new List<int>();
        private int work;
        internal Builder(byte[] sequence,byte[] instruments){seq=sequence;bank=instruments;}
        private int Read(ref int pc,int bytes)
        {
            if(pc<0||pc>seq.Length-bytes)throw new InvalidDataException("Menu sequence operand outside extent.");
            int v=0;for(int i=0;i<bytes;i++)v=(v<<8)|seq[pc++];return v;
        }
        internal void Track(int pc,int depth)
        {
            if(depth>1||tracks.Count>=3)throw new InvalidDataException("Menu track budget.");
            int id=tracks.Count;var t=new int[9];t[0]=32768;t[1]=12;
            int instrument=(int)U32(bank,36+7*4),osc=(int)U32(bank,instrument+16),release=(int)U32(bank,osc+12);
            t[7]=U16(bank,release+2);t[8]=U16(bank,release);tracks.Add(t);
            sends.Add(depth==0?8128:4064);
            int tick=0,program=-1,transpose=0;var active=new int[8];for(int i=0;i<8;i++)active[i]=-1;
            var calls=new int[4];int callDepth=0;
            for(;;)
            {
                if(++work>512||tick>2000)throw new InvalidDataException("Menu sequence work budget.");
                int op=Read(ref pc,1);
                if(op<0x80)
                {
                    int slot=Read(ref pc,1),velocity=Read(ref pc,1);
                    if(slot<1||slot>7||velocity>127||program!=7&&program!=9||active[slot]>=0||notes.Count>=32)
                        throw new InvalidDataException("Unsupported menu note.");
                    int source=program==7?0:1;
                    active[slot]=notes.Count;notes.Add(new[]{id,source,op+transpose,velocity,tick,-1,48,0});
                }
                else if(op==0x80||op==0x88)tick=FirmwareAudioBounds.Add(tick,Read(ref pc,op==0x80?1:2));
                else if(op>=0x81&&op<=0x87)
                {
                    int slot=op-0x80,n=active[slot];if(n<0)throw new InvalidDataException("Menu note-off without note.");
                    notes[n][5]=tick;active[slot]=-1;
                }
                else if(op==0x98||op==0x9a||op==0x9e)
                {
                    int param=Read(ref pc,1),value=Read(ref pc,op==0x9e?2:1),duration=op==0x98?0:Read(ref pc,1);
                    value=op==0x9e?(short)value:(short)(value<<8);
                    if(param==0&&tick==0&&duration==0)t[0]=value;
                    else if(param==2&&duration==0){if(value!=0)throw new InvalidDataException("Unsupported menu send.");}
                    else if(param==1||param==3)
                    {if(curves.Count>=64)throw new InvalidDataException("Menu automation budget.");curves.Add(new[]{id,param,tick,duration,value});}
                    else throw new InvalidDataException("Unsupported menu parameter.");
                }
                else if(op==0xa4||op==0xac)
                {
                    int reg=Read(ref pc,1),value=Read(ref pc,op==0xa4?1:2);
                    if(reg==6){program=value;if(program!=7&&program!=9)throw new InvalidDataException("Unsupported menu instrument.");}
                    else if(reg==7)t[1]=value;
                    else if(reg<8||reg>12||value!=(reg==10?32767:0))throw new InvalidDataException("Unsupported menu register.");
                    else if(reg==12)sends[id]=0;
                }
                else if(op==0xc1)
                {
                    Read(ref pc,1);int child=Read(ref pc,3);if(tick!=0)throw new InvalidDataException("Delayed menu track.");Track(child,depth+1);
                }
                else if(op==0xc4)
                {if(Read(ref pc,1)!=0||callDepth==calls.Length)throw new InvalidDataException("Menu call budget.");int target=Read(ref pc,3);calls[callDepth++]=pc;pc=target;}
                else if(op==0xc6||op==0xc5)
                {if(op==0xc6&&Read(ref pc,1)!=0||callDepth==0)throw new InvalidDataException("Menu return.");pc=calls[--callDepth];}
                else if(op==0xd9)transpose=(sbyte)Read(ref pc,1);
                else if(op==0xd8)
                {for(int i=0;i<5;i++)t[2+i]=Read(ref pc,2);t[7]=t[6]&0x3fff;t[8]=t[6]>>14;t[6]=1;}
                else if(op==0xf0){if(Read(ref pc,1)!=0)throw new InvalidDataException("Unsupported oscillator route.");}
                else if(op==0xff)
                {if(callDepth!=0)throw new InvalidDataException("Unclosed menu call.");for(int i=1;i<8;i++)if(active[i]>=0)throw new InvalidDataException("Unreleased menu note.");return;}
                else throw new InvalidDataException("Unsupported menu sequence command.");
            }
        }
        internal FirmwareMenuRecipe Finish(byte[]? reverb,byte[]? coefficients)
        {
            if(notes.Count==0)throw new InvalidDataException("Empty menu effect.");
            int frames=0;bool wet=false;
            foreach(var n in notes)
            {
                var t=tracks[n[0]];
                if(n[2]<0||n[2]>127||n[5]<=n[4]||t[1]>24||t[7]>6000||t[8]>2)throw new InvalidDataException("Invalid menu note bounds.");
                int end=(int)(((long)n[5]*64057+47999)/48000+((long)t[7]*64057+95999)/96000)*80;
                frames=Math.Max(frames,end);
                n[7]=sends[n[0]];if(n[7]!=0)wet=true;
            }
            wet=wet&&reverb!=null;
            if(wet)frames=FirmwareAudioBounds.Add(frames,32000);
            if(frames<1||frames>32000*8)throw new InvalidDataException("Menu output budget.");
            var data=new byte[64+tracks.Count*36+notes.Count*32+curves.Count*20+(wet?64:0)];
            Put(data,0,0x444d4531);Put(data,8,frames);Put(data,12,tracks.Count);Put(data,16,notes.Count);Put(data,20,curves.Count);
            int sourceCount=1;foreach(var n in notes)sourceCount=Math.Max(sourceCount,n[1]+1);Put(data,48,sourceCount);
            if(wet){Put(data,52,1);Put(data,56,32000);}
            if(coefficients!=null){if(coefficients.Length!=512)throw new InvalidDataException("Menu resampler table extent.");Put(data,60,1);}
            int at=64;foreach(var t in tracks)foreach(int v in t){Put(data,at,v);at+=4;}
            foreach(var n in notes)foreach(int v in n){Put(data,at,v);at+=4;}
            foreach(var c in curves)foreach(int v in c){Put(data,at,v);at+=4;}
            if(wet)Array.Copy(reverb!,0,data,at,64);
            return new FirmwareMenuRecipe(data,frames,coefficients);
        }
    }
    internal static void Put(byte[] d,int at,int n){d[at]=(byte)(n>>24);d[at+1]=(byte)(n>>16);d[at+2]=(byte)(n>>8);d[at+3]=(byte)n;}
    internal byte[] Packet(FirmwareMenuWaveSource[] sources,CancellationToken cancellation)
    {
        int count=Data[51],bytes=FirmwareAudioBounds.Add(Data.Length,resampling==null?0:512);for(int i=0;i<count;i++)bytes=FirmwareAudioBounds.Add(bytes,sources[i].EncodedBytes);
        var packet=new byte[bytes];
        try
        {
            cancellation.ThrowIfCancellationRequested();Array.Copy(Data,packet,Data.Length);Put(packet,4,bytes);
            int at=Data.Length;
            if(resampling!=null){Array.Copy(resampling,0,packet,at,512);at+=512;}
            for(int i=0;i<count;i++)
            {
                cancellation.ThrowIfCancellationRequested();var s=sources[i];
                Put(packet,24+i*12,at);Put(packet,28+i*12,at+s.Header.Length);Put(packet,32+i*12,s.Body.Length);
                Array.Copy(s.Header,0,packet,at,s.Header.Length);at+=s.Header.Length;
                for(int part=0;part<s.Body.Length;)
                {cancellation.ThrowIfCancellationRequested();int n=Math.Min(16384,s.Body.Length-part);Array.Copy(s.Body,part,packet,at,n);part+=n;at+=n;}
            }
            cancellation.ThrowIfCancellationRequested();return packet;
        }
        catch {Array.Clear(packet,0,packet.Length);throw;}
    }
    internal void Release(){Array.Clear(Data,0,Data.Length);if(resampling!=null)Array.Clear(resampling,0,resampling.Length);}
}
