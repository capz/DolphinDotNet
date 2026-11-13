using System;
using System.IO;
using System.Threading;

namespace Dolphin.System;

// The AOT backend does not support add.ovf/conv.ovf. Prove the bounds before
// ordinary arithmetic instead of relying on unsupported checked IL opcodes.
internal static class FirmwareAudioBounds
{
    internal static int Add(int a,int b)
    {if(a<0||b<0||a>int.MaxValue-b)throw new InvalidDataException("Firmware audio integer overflow.");return a+b;}
}

/// <summary>Additional resources to retain before disposing an IPL image.</summary>
[Flags]
public enum FirmwareCatalogOptions
{
    None=0,
    IncludeMenuAudio=1
}

/// <summary>IPL menu actions. Values are stable SDK identities, not wave or
/// event indices. An identity's presence does not imply firmware support.</summary>
public enum SystemMenuSound
{
    TurnSide=0, TurnCenter=1, Select=2, Back=3, OptionCancel=4,
    OptionConfirm=5, OptionMove1=6, OptionMove2=7, OptionMove3=8,
    OptionSelect=9, PressStart=10
}

/// <summary>Immutable metadata for a semantically verified complete IPL effect.
/// Rates and frames describe playback, not an instrument's source tuning.</summary>
public sealed class SystemMenuSoundInfo
{
    public SystemMenuSound Sound {get;}
    public int Channels {get;}
    public int SampleRate {get;}
    public int SampleFrameCount {get;}
    public double DurationSeconds=>(double)SampleFrameCount/SampleRate;
    public string ProfileId {get;}
    public bool IsDirectWave {get;}
    internal SystemMenuSoundInfo(SystemMenuSound sound,int channels,int rate,int frames,string profile,bool direct)
    {Sound=sound;Channels=channels;SampleRate=rate;SampleFrameCount=frames;ProfileId=profile;IsDirectWave=direct;}
}

/// <summary>Owned selected-wave snapshot in the extended format understood by
/// the native JAudio reader. Not a public semantic sound mapping.</summary>
internal sealed class FirmwareMenuWaveSource : IDisposable
{
    internal readonly byte[] Header;
    internal readonly byte[] Body;
    internal int EncodedBytes=>FirmwareAudioBounds.Add(Header.Length,Body.Length);
    private FirmwareMenuWaveSource(byte[] header,byte[] body){Header=header;Body=body;}
    internal static FirmwareMenuWaveSource Capture(byte[] image,FirmwareAudioArchive archive,int index,CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if(index<0||index>=archive.System.Count)throw new ArgumentOutOfRangeException(nameof(index));
        var wave=archive.System[index];
        if(wave.ArchiveName!=archive.Name)throw new InvalidDataException("Selected wave belongs to another archive.");
        if(archive.Offset<0||archive.Length<0||archive.Offset>image.Length||archive.Length>image.Length-archive.Offset||
           wave.Offset>archive.Length||wave.EncodedBytes>archive.Length-wave.Offset)
            throw new InvalidDataException("Selected menu wave exceeds validated archive.");
        // The parser validated all codec-dependent frame bounds. Keep only the
        // frames containing actual samples, including a partial final frame.
        int spf=wave.Codec<2?16:1,frameBytes=wave.Codec==0?9:wave.Codec==1?5:wave.Codec==2?1:2;
        long required=((long)wave.SampleCount+spf-1)/spf*frameBytes;
        if(required<1||required>wave.EncodedBytes)throw new InvalidDataException("Selected menu wave frame bounds.");
        int bytes=(int)required;
        var header=new byte[256];var body=new byte[bytes];
        try
        {
            Tag(header,0,"WSYS");Big(header,4,header.Length);Big(header,16,32);
            Tag(header,32,"WINF");Big(header,36,1);Big(header,40,64);
            Tag(header,64,"selected.aw");Big(header,176,1);Big(header,180,192);
            // Preserve all decoder/instrument fields present in the source
            // descriptor; compact descriptors receive zeroed extended fields.
            Array.Copy(image,FirmwareAudioBounds.Add(archive.System.Offset,wave.DescriptorOffset),header,192,wave.DescriptorBytes);
            Big(header,200,0);Big(header,204,bytes);
            int source=FirmwareAudioBounds.Add(archive.Offset,wave.Offset);
            for(int at=0;at<bytes;)
            {
                cancellation.ThrowIfCancellationRequested();int count=Math.Min(16384,bytes-at);
                Array.Copy(image,source+at,body,at,count);at+=count;
            }
            cancellation.ThrowIfCancellationRequested();
            return new FirmwareMenuWaveSource(header,body);
        }
        catch {Array.Clear(header,0,header.Length);Array.Clear(body,0,body.Length);throw;}
    }
    internal FirmwareMenuWaveSource Snapshot(CancellationToken cancellation)
    {
        var header=new byte[Header.Length];var body=new byte[Body.Length];
        try
        {
            cancellation.ThrowIfCancellationRequested();Array.Copy(Header,header,header.Length);
            for(int at=0;at<body.Length;)
            {
                cancellation.ThrowIfCancellationRequested();int count=Math.Min(16384,body.Length-at);
                Array.Copy(Body,at,body,at,count);at+=count;
            }
            cancellation.ThrowIfCancellationRequested();return new FirmwareMenuWaveSource(header,body);
        }
        catch {Array.Clear(header,0,header.Length);Array.Clear(body,0,body.Length);throw;}
    }
    private static void Tag(byte[] d,int at,string tag){for(int i=0;i<tag.Length;i++)d[at+i]=(byte)tag[i];}
    private static void Big(byte[] d,int at,int n){d[at]=(byte)(n>>24);d[at+1]=(byte)(n>>16);d[at+2]=(byte)(n>>8);d[at+3]=(byte)n;}
    public void Dispose(){Array.Clear(Header,0,Header.Length);Array.Clear(Body,0,Body.Length);}
}

/// <summary>Fail-closed semantic profile registry. Structural archive discovery
/// and a successful sample audition never qualify an action-to-wave mapping.</summary>
internal sealed class FirmwareMenuAudio : IDisposable
{
    internal readonly FirmwareAudioArchive[] Archives;
    internal readonly string UnsupportedReason;
    // No profile has yet met the event-trace/playback research gate. Do not add
    // a duration/count/region based mapping here. See the research matrix.
    private readonly SystemMenuSoundInfo?[] effects=new SystemMenuSoundInfo?[11];
    private readonly FirmwareMenuWaveSource?[] sources=new FirmwareMenuWaveSource?[11];
    internal int RetainedBytes {get;private set;}
    private FirmwareMenuAudio(FirmwareAudioArchive[] archives,string reason){Archives=archives;UnsupportedReason=reason;}
    internal static FirmwareMenuAudio Inspect(byte[] image,FirmwareFileTable? files,FirmwareWaveSystemInfo[] systems,
        FirmwareResource[] resources,CancellationToken cancellation)
    {
        var archives=FirmwareAudioArchive.Discover(image,files,systems,resources,cancellation);
        cancellation.ThrowIfCancellationRequested();
        return new FirmwareMenuAudio(archives,archives.Length==0?
            "No validated menu AW archive association; no supported IPL menu semantic profile.":
            "Validated AW samples are available for research, but no IPL menu event-to-wave profile is verified.");
    }
    internal static void Validate(SystemMenuSound sound)
    {if(sound<SystemMenuSound.TurnSide||sound>SystemMenuSound.PressStart)throw new ArgumentOutOfRangeException(nameof(sound));}
    internal bool TryGetInfo(SystemMenuSound sound,out SystemMenuSoundInfo? info)
    {info=effects[(int)sound];return info!=null;}
    internal FirmwareMenuWaveSource Snapshot(SystemMenuSound sound,CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();var source=sources[(int)sound];
        if(source==null)throw new NotSupportedException(UnsupportedReason);
        return source.Snapshot(cancellation);
    }
    public void Dispose(){foreach(var source in sources)source?.Dispose();RetainedBytes=0;}
}
