using System;
using System.IO;
using System.Threading;

namespace Dolphin.System;

// Complete score data is opt-in. Native preparation consumes one bounded owned
// snapshot; no producer thread can retain a managed array or firmware image.
internal sealed class FirmwareSystemAudio : IDisposable
{
    internal readonly FirmwareAudioArchive Archive;
    internal readonly string ProfileId;
    private readonly byte[] sequence,bank,coefficients,effects;
    internal int AdditionalRetainedBytes=>sequence.Length+bank.Length+effects.Length;
    internal FirmwareSystemAudio(FirmwareAudioArchive archive,string profile,byte[] sequence,byte[] bank,byte[] coefficients,byte[] finiteEffects)
    {
        Archive=archive;ProfileId=profile;this.sequence=sequence;this.bank=bank;this.coefficients=coefficients;
        if(coefficients.Length!=512)throw new NotSupportedException("Unqualified IPL resampler.");
        if(profile=="ipl-menu-early-v1"&&finiteEffects.Length==64)
        {
            effects=new byte[128];Array.Copy(finiteEffects,0,effects,0,64);
            for(int channel=0;channel<2;channel++)
            {
                int at=64+channel*32;effects[at]=2;effects[at+7]=1;
                effects[at+4+channel*4]=127;effects[at+5+channel*4]=255;
                FirmwareMenuRecipe.Put(effects,at+12,30);
            }
        }
        else if(profile=="ipl-menu-bms-v2"&&finiteEffects.Length==128)
            effects=(byte[])finiteEffects.Clone();
        else throw new NotSupportedException("Complete IPL DSP routing is not qualified for this profile.");
    }
    internal FirmwareSystemAudioSnapshot Snapshot(FirmwareAudioSamples samples,CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var selected=new FirmwareMenuWaveSource[12];var info=new FirmwareAudioSampleInfo[12];
        int bytes=64+12*24+sequence.Length+bank.Length+coefficients.Length+effects.Length;
        for(int id=0;id<12;id++)
        {
            int index=samples.FindQualifiedIndex(Archive,id);
            info[id]=samples[index];selected[id]=samples.SourceAt(index);
            if(info[id].Codec!=0||info[id].BankIndex!=0||info[id].SampleCount>200000)
                throw new InvalidDataException("Unsupported complete IPL sample bank.");
            bytes=FirmwareAudioBounds.Add(bytes,selected[id].EncodedBytes);
        }
        if(bytes>FirmwareCatalog.MaximumDecodedBytes)throw new InvalidDataException("Complete IPL snapshot exceeds budget.");
        var packet=new byte[bytes];
        try
        {
            FirmwareMenuRecipe.Put(packet,0,0x44494131);FirmwareMenuRecipe.Put(packet,4,bytes);
            FirmwareMenuRecipe.Put(packet,8,ProfileId=="ipl-menu-early-v1"?1:2);FirmwareMenuRecipe.Put(packet,12,12);
            FirmwareMenuRecipe.Put(packet,16,sequence.Length);FirmwareMenuRecipe.Put(packet,20,bank.Length);
            FirmwareMenuRecipe.Put(packet,24,4);FirmwareMenuRecipe.Put(packet,28,512);
            int at=64+12*24;
            foreach(var part in new[]{sequence,bank,effects,coefficients})
            {cancellation.ThrowIfCancellationRequested();Array.Copy(part,0,packet,at,part.Length);at+=part.Length;}
            for(int id=0;id<12;id++)
            {
                var source=selected[id];int table=64+id*24;
                FirmwareMenuRecipe.Put(packet,table,at);FirmwareMenuRecipe.Put(packet,table+4,at+source.Header.Length);
                FirmwareMenuRecipe.Put(packet,table+8,source.Body.Length);FirmwareMenuRecipe.Put(packet,table+12,id);
                FirmwareMenuRecipe.Put(packet,table+16,info[id].BaseKey);FirmwareMenuRecipe.Put(packet,table+20,source.Header.Length);
                foreach(var part in new[]{source.Header,source.Body})
                {
                    for(int offset=0;offset<part.Length;)
                    {
                        cancellation.ThrowIfCancellationRequested();int count=Math.Min(16384,part.Length-offset);
                        Array.Copy(part,offset,packet,at,count);at+=count;offset+=count;
                    }
                }
            }
            cancellation.ThrowIfCancellationRequested();return new FirmwareSystemAudioSnapshot(packet,info,ProfileId);
        }
        catch {Array.Clear(packet,0,packet.Length);throw;}
    }
    public void Dispose(){Array.Clear(sequence,0,sequence.Length);Array.Clear(bank,0,bank.Length);Array.Clear(effects,0,effects.Length);}
}

internal sealed class FirmwareSystemAudioSnapshot : IDisposable
{
    internal readonly byte[] Packet;
    internal readonly FirmwareAudioSampleInfo[] Samples;
    internal readonly string ProfileId;
    internal FirmwareSystemAudioSnapshot(byte[] packet,FirmwareAudioSampleInfo[] samples,string profile)
    {Packet=packet;Samples=samples;ProfileId=profile;}
    public void Dispose()=>Array.Clear(Packet,0,Packet.Length);
}
