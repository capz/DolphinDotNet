using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using static Dolphin.Formats.GameCubeBinary;

namespace Dolphin.System;

/// <summary>Wave metadata from a bounded WSYS descriptor. Offset is relative to
/// its named AW archive, never an assumed fixed ROM address.</summary>
public sealed class FirmwareWave
{
    public string ArchiveName {get;}
    public int Codec {get;}
    public int SampleRate {get;}
    public int SampleCount {get;}
    public int Offset {get;}
    public int EncodedBytes {get;}
    public bool HasLoop {get;}
    public int LoopStart {get;}
    public int LoopEnd {get;}
    internal FirmwareWave(string archive,int codec,int rate,int samples,int offset,int bytes,bool loop,int start,int end)
    {ArchiveName=archive;Codec=codec;SampleRate=rate;SampleCount=samples;Offset=offset;EncodedBytes=bytes;HasLoop=loop;LoopStart=start;LoopEnd=end;}
}

public static class FirmwareWaveSystem
{
    /// <summary>Inspect a standalone WSYS. Counts and pointers are bounded by
    /// its declared extent, not by unrelated trailing ROM data. No AFC decoding
    /// is duplicated here; playback uses the existing native decoder.</summary>
    public static FirmwareWave[] Inspect(byte[] data,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Check(data,0,32);
        if(!FirmwareResource.Match(data,0,"WSYS"))throw new NotSupportedException("Expected WSYS descriptor.");
        int size=AssetInt(U32(data,4)),winf=AssetInt(U32(data,16));
        if(size<32||size>data.Length||size>FirmwareCatalog.MaximumDecodedBytes||winf<24||winf>size-8||!FirmwareResource.Match(data,winf,"WINF"))
            throw new InvalidDataException("Invalid WSYS extent or WINF table.");
        int banks=AssetInt(U32(data,winf+4));
        if(banks<1||banks>256||banks>(size-winf-8)/4)throw new InvalidDataException("Invalid WSYS bank count.");
        // Early IPL WSYS uses 68-byte bank names and 36-byte descriptors;
        // later images use 112-byte names and 44-byte descriptors. Resolve the
        // layout from all bounded tables, not a region label or first wave.
        bool compact=BankLayout(data,size,winf,banks,68,36,cancellationToken);
        bool extended=BankLayout(data,size,winf,banks,112,44,cancellationToken);
        if(compact==extended)throw new InvalidDataException("Ambiguous or invalid WSYS bank layout.");
        int nameBytes=compact?68:112,descriptorBytes=compact?36:44;
        var waves=new List<FirmwareWave>();
        for(int b=0;b<banks;b++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int bank=AssetInt(U32(data,winf+8+b*4));
            if(bank<24||bank>size-nameBytes-4)throw new InvalidDataException("WSYS bank outside descriptor.");
            int end=bank;
            while(end<bank+nameBytes&&data[end]!=0){if(data[end]<32||data[end]>126)throw new InvalidDataException("Invalid AW archive name.");end++;}
            if(end==bank||end==bank+nameBytes)throw new InvalidDataException("Unterminated AW archive name.");
            string name=Ascii(data,bank,end-bank);
            int count=AssetInt(U32(data,bank+nameBytes));
            if(count<1||count>4096||count>(size-bank-nameBytes-4)/4||count>8192-waves.Count)
                throw new InvalidDataException("WSYS wave count exceeds budget.");
            for(int w=0;w<count;w++)
            {
                if((w&63)==0)cancellationToken.ThrowIfCancellationRequested();
                int at=AssetInt(U32(data,bank+nameBytes+4+w*4));
                if(at<24||at>size-descriptorBytes)throw new InvalidDataException("WSYS wave descriptor outside extent.");
                int codec=data[at+1];float rate=F32(data,at+4);
                if(codec>3||float.IsNaN(rate)||float.IsInfinity(rate)||rate<5000||rate>48000||rate!=(int)rate)
                    throw new NotSupportedException("Unsupported WSYS codec or sample rate.");
                int offset=AssetInt(U32(data,at+8)),bytes=AssetInt(U32(data,at+12)),samples=AssetInt(U32(data,at+28));
                uint loop=U32(data,at+16);
                int start=loop!=0?AssetInt(U32(data,at+20)):0,stop=loop!=0?AssetInt(U32(data,at+24)):samples;
                int spf=codec<2?16:1,frameBytes=codec==0?9:codec==1?5:codec==2?1:2;
                if(samples<1||samples>int.MaxValue/4||bytes<1||bytes>FirmwareCatalog.MaximumDecodedBytes||
                   ((long)samples+spf-1)/spf*frameBytes>bytes||loop!=0&&loop!=uint.MaxValue||
                   loop!=0&&(start>=stop||stop>samples))throw new InvalidDataException("Invalid WSYS sample/loop ranges.");
                waves.Add(new FirmwareWave(name,codec,(int)rate,samples,offset,bytes,loop!=0,start,loop!=0?stop:samples));
            }
        }
        return waves.ToArray();
    }
    private static bool BankLayout(byte[] data,int size,int winf,int banks,int nameBytes,int descriptorBytes,CancellationToken cancellation)
    {
        try
        {
            int total=0;
            for(int b=0;b<banks;b++)
            {
                cancellation.ThrowIfCancellationRequested();
                int bank=AssetInt(U32(data,winf+8+b*4));
                if(bank<24||bank>size-nameBytes-4)return false;
                int end=bank;
                while(end<bank+nameBytes&&data[end]!=0)
                {if(data[end]<32||data[end]>126)return false;end++;}
                if(end==bank||end==bank+nameBytes)return false;
                int count=AssetInt(U32(data,bank+nameBytes));
                if(count<1||count>4096||count>(size-bank-nameBytes-4)/4||count>8192-total)return false;
                total+=count;
                for(int w=0;w<count;w++)
                {
                    if((w&63)==0)cancellation.ThrowIfCancellationRequested();
                    int at=AssetInt(U32(data,bank+nameBytes+4+w*4));
                    if(at<24||at>size-descriptorBytes)return false;
                }
            }
            return true;
        }
        catch(InvalidDataException){return false;}
    }
}

/// <summary>Validated association between a descriptor and its sample archive.</summary>
public enum FirmwareAudioLayout { ExplicitPair, FileTable, AdjacentWaveArchive }

/// <summary>Owned original stereo AFC channels with validated WSYS metadata.
/// The packet retains compressed samples, never a full ROM or temporary file.</summary>
public sealed class FirmwareStereoAudio
{
    private byte[]? packet;
    public int SampleRate {get;}
    public int SampleCount {get;}
    public double DurationSeconds=>(double)SampleCount/SampleRate;
    public bool HasLoop {get;}
    public int LeftArchiveOffset {get;}
    public int RightArchiveOffset {get;}
    public FirmwareAudioLayout Layout {get;private set;}
    /// <summary>ROM-relative AW position, or -1 for an explicitly supplied archive.</summary>
    public int FirmwareOffset {get;private set;}=-1;
    public int ArchiveBytes {get;private set;}
    public int CompressedBytes=>packet==null?0:packet.Length;
    private FirmwareStereoAudio(byte[] bytes,FirmwareWave left,FirmwareWave right)
    {packet=bytes;SampleRate=left.SampleRate;SampleCount=left.SampleCount;HasLoop=left.HasLoop;LeftArchiveOffset=left.Offset;RightArchiveOffset=right.Offset;}
    public byte[] ExtractAfc(){var data=packet;if(data==null)throw new ObjectDisposedException(nameof(FirmwareStereoAudio));var result=new byte[data.Length];Array.Copy(data,result,result.Length);return result;}
    internal void Release(){var data=packet;if(data==null)return;Array.Clear(data,0,data.Length);packet=null;}
    /// <summary>Combine two metadata-selected AFC channels in original archive
    /// order. It rejects overlaps, unequal formats/rates/durations or loops.
    /// Interleaving is lossless and reuses the native AFC reader unchanged.</summary>
    public static FirmwareStereoAudio Read(byte[] waveSystem,byte[] archive,int leftIndex,int rightIndex,CancellationToken cancellationToken=default)
    {
        if(archive==null)throw new ArgumentNullException(nameof(archive));
        if(archive.Length>FirmwareCatalog.MaximumDecodedBytes)throw new InvalidDataException("AW exceeds firmware budget.");
        var waves=FirmwareWaveSystem.Inspect(waveSystem,cancellationToken);
        if(leftIndex<0||rightIndex<0||leftIndex>=waves.Length||rightIndex>=waves.Length||leftIndex==rightIndex)
            throw new ArgumentOutOfRangeException(nameof(leftIndex));
        var left=waves[leftIndex];var right=waves[rightIndex];
        if(left.Codec!=0||right.Codec!=0||left.ArchiveName!=right.ArchiveName||left.SampleRate!=right.SampleRate||
           left.SampleCount!=right.SampleCount||left.HasLoop!=right.HasLoop||left.HasLoop&&(left.LoopStart!=right.LoopStart||left.LoopEnd!=right.LoopEnd))
            throw new NotSupportedException("WSYS entries are not a compatible stereo AFC pair.");
        Check(archive,left.Offset,left.EncodedBytes);Check(archive,right.Offset,right.EncodedBytes);
        if(left.Offset<right.Offset+right.EncodedBytes&&right.Offset<left.Offset+left.EncodedBytes)
            throw new InvalidDataException("Stereo AFC channel ranges overlap.");
        int frames=(left.SampleCount+15)/16;
        if(frames>(FirmwareCatalog.MaximumDecodedBytes-32)/18)throw new InvalidDataException("Stereo AFC exceeds firmware budget.");
        var packet=new byte[32+frames*18];
        Write32(packet,0,packet.Length);Write32(packet,4,left.SampleCount);
        packet[8]=(byte)(left.SampleRate>>8);packet[9]=(byte)left.SampleRate;packet[11]=4;packet[13]=16;
        Write32(packet,16,left.HasLoop?1:0);Write32(packet,20,left.LoopStart);
        // The legacy AFC container expresses a loop through sample count; it
        // cannot represent an earlier exclusive loop end without losing frames.
        if(left.HasLoop&&left.LoopEnd!=left.SampleCount)throw new NotSupportedException("AFC container cannot preserve this loop end.");
        for(int f=0;f<frames;f++)
        {if((f&255)==0)cancellationToken.ThrowIfCancellationRequested();Array.Copy(archive,left.Offset+f*9,packet,32+f*18,9);Array.Copy(archive,right.Offset+f*9,packet,41+f*18,9);}
        return new FirmwareStereoAudio(packet,left,right){ArchiveBytes=archive.Length};
    }
    private static bool StartupWave(FirmwareWave wave)
        =>wave.Codec==0&&wave.SampleRate==32000&&!wave.HasLoop&&
          wave.SampleCount>174064&&wave.SampleCount<=174080;
    internal static FirmwareStereoAudio? Discover(byte[] image,FirmwareFileTable? table,FirmwareWaveSystemInfo[] systems,CancellationToken cancellation)
    {
        FirmwareStereoAudio? found=null;int work=8192,pairs=0;
        for(int file=0;table!=null&&file<table.Count;file++)
        {
            cancellation.ThrowIfCancellationRequested();
            var entry=table[file];if(!FirmwareResource.Match(image,entry.Offset,"WSYS"))continue;
            var descriptor=table.Extract(image,file);FirmwareWave[] waves;
            try{waves=FirmwareWaveSystem.Inspect(descriptor,cancellation);}catch(InvalidDataException){continue;}catch(NotSupportedException){continue;}
            work-=waves.Length;if(work<0)throw new InvalidDataException("Firmware wave discovery budget exceeded.");
            int eligible=0;
            foreach(var wave in waves)if(StartupWave(wave))eligible++;
            if(eligible>64)throw new InvalidDataException("Too many candidate startup waves.");
            for(int first=0;first<waves.Length;first++)
            {
                var left=waves[first];
                // Startup duration/rate are profile hints, backed by descriptor,
                // named archive resolution, disjoint ranges and a unique pair.
                if(!StartupWave(left))continue;
                for(int second=first+1;second<waves.Length;second++)
                {
                    var right=waves[second];
                    if(right.Codec!=0||right.ArchiveName!=left.ArchiveName||right.SampleRate!=left.SampleRate||right.SampleCount!=left.SampleCount)continue;
                    if(++pairs>32)throw new InvalidDataException("Startup audio candidate budget exceeded.");
                    int archiveIndex=-1;
                    for(int j=0;j<table.Count;j++)if(table[j].Path==left.ArchiveName||Leaf(table[j].Path)==Leaf(left.ArchiveName))
                    {if(archiveIndex>=0)throw new InvalidDataException("Ambiguous AW archive mapping.");archiveIndex=j;}
                    if(archiveIndex<0)continue;
                    var archive=table.Extract(image,archiveIndex);FirmwareStereoAudio pair;
                    try{pair=left.Offset<right.Offset?Read(descriptor,archive,first,second,cancellation):Read(descriptor,archive,second,first,cancellation);}
                    catch(InvalidDataException){continue;}catch(NotSupportedException){continue;}
                    pair.Layout=FirmwareAudioLayout.FileTable;
                    pair.FirmwareOffset=table[archiveIndex].Offset;
                    if(found!=null){found.Release();pair.Release();throw new InvalidDataException("Ambiguous startup stereo wave pair.");}found=pair;
                }
            }
        }
        foreach(var system in systems)
        {
            cancellation.ThrowIfCancellationRequested();
            var candidate=ReadAdjacentArchive(image,system,cancellation);
            if(candidate==null)continue;
            if(found!=null)
            {
                bool same=found.FirmwareOffset==candidate.FirmwareOffset&&found.ArchiveBytes==candidate.ArchiveBytes&&
                    found.SampleRate==candidate.SampleRate&&found.SampleCount==candidate.SampleCount&&
                    found.LeftArchiveOffset==candidate.LeftArchiveOffset&&found.RightArchiveOffset==candidate.RightArchiveOffset;
                candidate.Release();
                if(same)continue;
                found.Release();throw new InvalidDataException("Ambiguous original startup archive mapping.");
            }
            found=candidate;
        }
        return found;
    }
    private static FirmwareStereoAudio? ReadAdjacentArchive(byte[] image,FirmwareWaveSystemInfo system,CancellationToken cancellation)
    {
        // Original IPL profile: a numbered WSYS record followed by its single
        // packed AW bank. Verified in both 14-record NTSC and 31-record PAL
        // layouts. No ROM address or compressed-sample signature is a locator.
        if(system.Count!=12||system.Offset<32)return null;
        int header=system.Offset-32;
        if(U32(image,header)!=0||U32(image,header+4)!=0||U32(image,header+8)!=2||
           U32(image,header+20)!=0||U32(image,header+24)!=0||U32(image,header+28)!=0)return null;
        uint declared=U32(image,header+12),allocated=U32(image,header+16);
        // The early compact bank retains the later wrapper's stale size. Its
        // own bounded WSYS extent determines the next record, not that field.
        if(declared<(uint)system.Length||declared>FirmwareCatalog.MaximumDecodedBytes||
           allocated!=((declared+31)&~31u))return null;
        int archiveAt=(system.Offset+system.Length+31)&~31;
        if(archiveAt>0x1aff00)return null;
        int next=0,extent=0,startup=0;
        string archiveName=system[0].ArchiveName;
        if(Leaf(archiveName)!="ipl_0.aw")return null;
        for(int i=0;i<system.Count;i++)
        {
            cancellation.ThrowIfCancellationRequested();
            var wave=system[i];
            if(wave.ArchiveName!=archiveName||wave.Codec!=0||wave.Offset!=next||
               wave.EncodedBytes!=((long)wave.SampleCount+15)/16*9||
               wave.Offset>FirmwareCatalog.MaximumDecodedBytes-wave.EncodedBytes)return null;
            extent=wave.Offset+wave.EncodedBytes;next=(extent+31)&~31;
            if(StartupWave(wave))startup++;
        }
        if(startup!=2||!StartupWave(system[0])||!StartupWave(system[1])||
           extent>0x1aff00-archiveAt)return null;
        // The descriptor's alignment fill is zero in this profile. Padding
        // between AW waves is unspecified and can contain nonzero build data;
        // the decoder reads only each metadata-defined wave extent.
        for(int i=system.Offset+system.Length;i<archiveAt;i++)if(image[i]!=0)return null;
        var descriptor=new byte[system.Length];Array.Copy(image,system.Offset,descriptor,0,descriptor.Length);
        var archive=new byte[extent];Array.Copy(image,archiveAt,archive,0,extent);
        var result=Read(descriptor,archive,0,1,cancellation);
        result.Layout=FirmwareAudioLayout.AdjacentWaveArchive;result.FirmwareOffset=archiveAt;
        return result;
    }
    private static string Leaf(string path){int at=path.LastIndexOf('/');return at<0?path:path.Substring(at+1);}
    private static void Write32(byte[] d,int a,int n){d[a]=(byte)(n>>24);d[a+1]=(byte)(n>>16);d[a+2]=(byte)(n>>8);d[a+3]=(byte)n;}
}

/// <summary>Detached metadata for a fully bounded WSYS found in the firmware.
/// Discovery does not infer an AW base address from compressed sample bytes.</summary>
public sealed class FirmwareWaveSystemInfo
{
    private readonly FirmwareWave[] waves;
    public int Offset {get;}
    public int Length {get;}
    public int Count=>waves.Length;
    public FirmwareWave this[int index]=>waves[index];
    internal FirmwareWaveSystemInfo(int offset,int length,FirmwareWave[] entries)
    {Offset=offset;Length=length;waves=entries;}
    internal static FirmwareWaveSystemInfo[] Scan(byte[] image,global::System.Threading.CancellationToken cancellation)
    {
        var result=new List<FirmwareWaveSystemInfo>();int work=0,totalWaves=0;
        for(int at=0x100;at<=0x1aff00-32;at+=4)
        {
            if((at&4095)==0)cancellation.ThrowIfCancellationRequested();
            if(!FirmwareResource.Match(image,at,"WSYS"))continue;
            uint size=U32(image,at+4);
            if(size<32||size>0x1aff00-at)continue;
            if(size>16*FirmwareCatalog.MaximumDecodedBytes-work)throw new InvalidDataException("WSYS scan work budget exceeded.");
            work+=(int)size;
            var bytes=new byte[(int)size];Array.Copy(image,at,bytes,0,bytes.Length);FirmwareWave[] entries;
            try{entries=FirmwareWaveSystem.Inspect(bytes,cancellation);}
            catch(InvalidDataException){continue;}catch(NotSupportedException){continue;}
            if(result.Count==128||entries.Length>8192-totalWaves)throw new InvalidDataException("WSYS metadata budget exceeded.");
            totalWaves+=entries.Length;result.Add(new FirmwareWaveSystemInfo(at,(int)size,entries));
        }
        return result.ToArray();
    }
}
