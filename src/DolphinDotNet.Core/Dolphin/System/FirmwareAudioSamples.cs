using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Dolphin.System;

internal sealed class FirmwareAudioSampleOwner {}

/// <summary>An immutable descriptor for one validated original instrument
/// sample. WaveId is the bank's C-DF identity, not a flattened sample index.
/// A sample is not a complete sequenced menu effect.</summary>
public sealed class FirmwareAudioSampleInfo
{
    internal readonly FirmwareAudioSampleOwner Owner;
    internal readonly int Index;
    internal readonly int FirmwareSampleOffset;
    public string ArchiveName {get;}
    public int WaveSystemIndex {get;}
    public int BankIndex {get;}
    public int WaveId {get;}
    public int BaseKey {get;}
    public int Codec {get;}
    public int Channels=>1;
    public int SampleRate {get;}
    public int SampleCount {get;}
    public bool HasLoop {get;}
    public int LoopStartSample {get;}
    public int LoopEndSample {get;}
    internal FirmwareAudioSampleInfo(FirmwareAudioSampleOwner owner,int index,int system,FirmwareWave wave,int offset)
    {
        Owner=owner;Index=index;WaveSystemIndex=system;ArchiveName=wave.ArchiveName;FirmwareSampleOffset=offset;
        BankIndex=wave.BankIndex;WaveId=wave.WaveId;BaseKey=wave.BaseKey;Codec=wave.Codec;
        SampleRate=wave.SampleRate;SampleCount=wave.SampleCount;HasLoop=wave.HasLoop;
        LoopStartSample=wave.LoopStart;LoopEndSample=wave.LoopEnd;
    }
}

// Own only bounded selected-wave snapshots, never the image. This layer makes
// no semantic claim about a raw source's use by an original sequence.
internal sealed class FirmwareAudioSamples : IDisposable
{
    private readonly FirmwareAudioSampleOwner owner=new FirmwareAudioSampleOwner();
    private readonly List<FirmwareMenuWaveSource> sources=new List<FirmwareMenuWaveSource>();
    private readonly List<bool> owned=new List<bool>();
    private readonly List<FirmwareAudioSampleInfo> samples=new List<FirmwareAudioSampleInfo>();
    private readonly SystemStartupSoundInfo?[] startup=new SystemStartupSoundInfo?[3];
    internal int RetainedBytes {get;private set;}
    internal int AdditionalRetainedBytes {get;private set;}
    internal int Count=>samples.Count;
    internal FirmwareAudioSampleInfo this[int index]=>samples[index];
    internal static FirmwareAudioSamples Capture(byte[] image,FirmwareFileTable? files,FirmwareWaveSystemInfo[] systems,
        FirmwareResource[] resources,int budget,FirmwareMenuAudio? menu,CancellationToken cancellation)
    {
        var result=new FirmwareAudioSamples();
        try
        {
            var archives=FirmwareAudioArchive.Discover(image,files,systems,resources,cancellation);
            for(int system=0;system<systems.Length;system++)
            for(int i=0;i<systems[system].Count;i++)
            {
                cancellation.ThrowIfCancellationRequested();var wave=systems[system][i];FirmwareAudioArchive? archive=null;
                foreach(var candidate in archives)
                    if(candidate.System==systems[system]&&candidate.Name==wave.ArchiveName)
                    {if(archive!=null)throw new InvalidDataException("Conflicting original audio sample association.");archive=candidate;}
                if(archive==null)continue;
                if(result.Count>=8192)throw new InvalidDataException("Original audio sample count exceeds budget.");
                int spf=wave.Codec<2?16:1,frameBytes=wave.Codec==0?9:wave.Codec==1?5:wave.Codec==2?1:2;
                long bytes=((long)wave.SampleCount+spf-1)/spf*frameBytes+256;
                var source=menu==null?null:menu.FindRetainedSource(archive,i);
                bool own=source==null;
                if(own&&bytes>budget-result.AdditionalRetainedBytes)throw new InvalidDataException("Original audio sample retention budget exceeded.");
                if(source==null)source=FirmwareMenuWaveSource.Capture(image,archive,i,cancellation);
                result.sources.Add(source);
                result.owned.Add(own);
                result.samples.Add(new FirmwareAudioSampleInfo(result.owner,result.Count,system,wave,FirmwareAudioBounds.Add(archive.Offset,wave.Offset)));
                result.RetainedBytes=FirmwareAudioBounds.Add(result.RetainedBytes,source.EncodedBytes);
                if(own)result.AdditionalRetainedBytes=FirmwareAudioBounds.Add(result.AdditionalRetainedBytes,source.EncodedBytes);
            }
            result.QualifyStartup(menu);
            cancellation.ThrowIfCancellationRequested();return result;
        }
        catch {result.Dispose();throw;}
    }
    private void QualifyStartup(FirmwareMenuAudio? menu)
    {
        var archive=menu==null?null:menu.QualifiedArchive;
        SystemMenuSoundInfo? profile;
        if(archive==null||menu==null||!menu.TryGetInfo(SystemMenuSound.Select,out profile)||profile==null)return;
        // Live controller selection and the original score's program table:
        // one Z selects program 2 (IDs 2/3); four Zs select program 1 (IDs 0/1).
        // These are deliberately independent of the stable SDK enum order.
        int[] leftIds={7,2,0},rightIds={8,3,1},counts={174067,166745,187092};
        for(int v=0;v<3;v++)
        {
            int left=-1,right=-1;
            for(int i=0;i<Count;i++)
            {
                var info=this[i];
                if(info.BankIndex!=0||info.ArchiveName!=archive.Name||info.FirmwareSampleOffset<archive.Offset||
                   info.FirmwareSampleOffset>=archive.Offset+archive.Length)continue;
                if(info.WaveId==leftIds[v]){if(left>=0)throw new InvalidDataException("Ambiguous startup left source.");left=i;}
                if(info.WaveId==rightIds[v]){if(right>=0)throw new InvalidDataException("Ambiguous startup right source.");right=i;}
            }
            if(left<0||right<0)continue;
            var l=this[left];var r=this[right];
            if(l.Codec!=0||r.Codec!=0||l.HasLoop||r.HasLoop||l.BaseKey!=60||r.BaseKey!=60||
               l.SampleRate!=32000||r.SampleRate!=32000||l.SampleCount!=counts[v]||r.SampleCount!=counts[v])
                throw new InvalidDataException("Startup source metadata differs from qualified profile.");
            startup[v]=new SystemStartupSoundInfo((SystemStartupSoundVariant)v,profile.ProfileId,32000,counts[v],leftIds[v],rightIds[v],left,right);
        }
    }
    internal SystemStartupSoundInfo? StartupInfo(SystemStartupSoundVariant variant)=>startup[(int)variant];
    internal byte[] SnapshotStartup(SystemStartupSoundVariant variant,CancellationToken cancellation)
    {
        var info=StartupInfo(variant);
        if(info==null)throw new NotSupportedException("No qualified original startup variant in this catalog.");
        return FirmwareStereoAudio.Interleave(sources[info.LeftIndex],sources[info.RightIndex],info.SampleCount,info.SampleRate,cancellation);
    }
    internal FirmwareMenuWaveSource? FindSource(int offset,int samples,int rate)
    {
        for(int i=0;i<Count;i++)
        {
            var info=this[i];
            if(info.FirmwareSampleOffset==offset&&info.Codec==0&&info.SampleCount==samples&&info.SampleRate==rate&&!info.HasLoop)
                return sources[i];
        }
        return null;
    }
    internal FirmwareMenuWaveSource SourceAt(int index)=>sources[index];
    internal int FindQualifiedIndex(FirmwareAudioArchive archive,int id)
    {
        int found=-1;
        for(int i=0;i<Count;i++)
        {
            var info=this[i];
            if(info.BankIndex!=0||info.WaveId!=id||info.ArchiveName!=archive.Name||
               info.FirmwareSampleOffset<archive.Offset||info.FirmwareSampleOffset>=archive.Offset+archive.Length)continue;
            if(found>=0)throw new InvalidDataException("Ambiguous complete IPL source ID.");found=i;
        }
        if(found<0)throw new InvalidDataException("Missing complete IPL source ID.");return found;
    }
    internal FirmwareMenuWaveSource Snapshot(FirmwareAudioSampleInfo info,CancellationToken cancellation)
    {
        if(info.Owner!=owner||info.Index<0||info.Index>=Count||samples[info.Index]!=info)
            throw new ArgumentException("The audio sample descriptor belongs to another catalog.",nameof(info));
        return sources[info.Index].Snapshot(cancellation);
    }
    public void Dispose(){for(int i=0;i<sources.Count;i++)if(i<owned.Count&&owned[i])sources[i].Dispose();RetainedBytes=0;AdditionalRetainedBytes=0;}
}
