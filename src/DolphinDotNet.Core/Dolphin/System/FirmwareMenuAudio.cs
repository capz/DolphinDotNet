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
    internal string UnsupportedReason {get;private set;}
    private readonly SystemMenuSoundInfo?[] effects=new SystemMenuSoundInfo?[11];
    private FirmwareMenuWaveSource[] sources=new FirmwareMenuWaveSource[0];
    private readonly FirmwareMenuRecipe?[] recipes=new FirmwareMenuRecipe?[11];
    internal int RetainedBytes {get;private set;}
    private FirmwareMenuAudio(FirmwareAudioArchive[] archives,string reason){Archives=archives;UnsupportedReason=reason;}
    internal static FirmwareMenuAudio Inspect(byte[] image,FirmwareFileTable? files,FirmwareWaveSystemInfo[] systems,
        FirmwareResource[] resources,CancellationToken cancellation)
    {
        var archives=FirmwareAudioArchive.Discover(image,files,systems,resources,cancellation);
        cancellation.ThrowIfCancellationRequested();
        var result=new FirmwareMenuAudio(archives,archives.Length==0?
            "No validated menu AW archive association; no supported IPL menu semantic profile.":
            "No supported IPL menu event/bank semantic fingerprint; secondary effects remain unavailable.");
        try
        {
            foreach(var archive in archives)
            {
                int bank=archive.System.Offset-0x8e0;
                int end=FirmwareAudioBounds.Add(archive.Offset,archive.Length);
                if(end>int.MaxValue-31)throw new InvalidDataException("Menu sequence alignment overflow.");
                int seq=(end+31)&~31;
                if(bank<0||!FirmwareResource.Match(image,bank,"IBNK")||
                   bank>image.Length-0x8e0||Read(image,bank+4)!=0x8e0||
                   FirmwareAudioFingerprint.Hash(image,bank,0x8e0,cancellation)!="50e79d0625aa0b8817509b6fa3f56af7c3f94aa81dac64a146e15e04f0ce10e1")continue;
                bool modern=seq<=image.Length-3117&&FirmwareAudioFingerprint.Hash(image,seq,3117,cancellation)=="3432f80f4f3c7be92e1288b81ce8071db3554cd8fcc520410f5dd489f0ccdc1f";
                if(!modern&&seq<=image.Length-3092&&FirmwareAudioFingerprint.Hash(image,seq,3092,cancellation)=="5e9be1050199629aa560f410effbbd5a8361ea17777fb07f580c1d75586cbd35")
                    result.UnsupportedReason="Early NTSC menu events match, but its different DSP-effect setup is not qualified.";
                // Early NTSC has the same instruments/events but a different
                // DSP-effect setup. Do not qualify its output from BMS alone.
                if(!modern||bank<128||FirmwareAudioFingerprint.Hash(image,bank-128,128,cancellation)!="c10b2ad8bc679484c74fe78f9b5bcca66e210a08cba43a51fbc5b36ad224b4fc")continue;
                result.UnsupportedReason="This IPL profile supports TurnSide, TurnCenter and Select; the requested secondary effect is unavailable.";
                if(result.sources.Length!=0)throw new InvalidDataException("Conflicting IPL menu profiles.");
                int first=ResolveId(image,archive,9),second=ResolveId(image,archive,11);
                var selected=new FirmwareMenuWaveSource[2];
                try
                {
                    selected[0]=FirmwareMenuWaveSource.Capture(image,archive,first,cancellation);
                    selected[1]=FirmwareMenuWaveSource.Capture(image,archive,second,cancellation);
                    result.sources=selected;
                }
                catch {foreach(var s in selected)s?.Dispose();throw;}
                var sequence=new byte[3117];Array.Copy(image,seq,sequence,0,sequence.Length);
                var instruments=new byte[0x8e0];Array.Copy(image,bank,instruments,0,instruments.Length);
                var reverb=new byte[64];Array.Copy(image,bank-128,reverb,0,64);
                byte[] resampling=CaptureResampler(image,bank,cancellation);
                result.RetainedBytes=FirmwareAudioBounds.Add(result.RetainedBytes,resampling.Length);
                int table=0x1bc;
                for(int i=0;i<3;i++)
                {
                    cancellation.ThrowIfCancellationRequested();int at=table+(i+3)*3;
                    int target=(sequence[at]<<16)|(sequence[at+1]<<8)|sequence[at+2];
                    var recipe=FirmwareMenuRecipe.Compile(sequence,target,instruments,reverb,resampling);result.recipes[i]=recipe;
                    result.effects[i]=new SystemMenuSoundInfo((SystemMenuSound)i,2,32000,recipe.Frames,"ipl-menu-bms-v1",false);
                    result.RetainedBytes=FirmwareAudioBounds.Add(result.RetainedBytes,recipe.Data.Length);
                }
                foreach(var s in selected)result.RetainedBytes=FirmwareAudioBounds.Add(result.RetainedBytes,s.EncodedBytes);
            }
            return result;
        }
        catch {result.Dispose();throw;}
    }
    private static int Read(byte[] data,int at)
    {if(at<0||at>data.Length-4)throw new InvalidDataException("Menu table extent.");uint v=((uint)data[at]<<24)|((uint)data[at+1]<<16)|((uint)data[at+2]<<8)|data[at+3];if(v>int.MaxValue)throw new InvalidDataException("Menu pointer overflow.");return (int)v;}
    private static byte[] CaptureResampler(byte[] image,int bank,CancellationToken cancellation)
    {
        int found=-1;
        for(int at=Math.Max(0,bank-65536);at<=bank-512;at+=4)
        {
            if((at&1023)==0)cancellation.ThrowIfCancellationRequested();
            if(image[at]!=12||image[at+1]!=57||image[at+2]!=102||image[at+3]!=173)continue;
            if(FirmwareAudioFingerprint.Hash(image,at,512,cancellation)!="f5010bc5d7798e7a059af43623dfcdb26c34b5bf2d282ad253db5bdc32ce591b")continue;
            // The IPL contains identical software/DSP copies. An exact full
            // table digest establishes equivalence; their positions are not
            // competing event or archive mappings.
            found=at;
        }
        if(found<0)throw new InvalidDataException("Missing qualified IPL resampling table.");
        var coefficients=new byte[512];Array.Copy(image,found,coefficients,0,512);return coefficients;
    }
    private static int ResolveId(byte[] image,FirmwareAudioArchive archive,int id)
    {
        int root=archive.System.Offset,size=archive.System.Length;
        int wbct=Read(image,root+20);
        if(wbct<24||wbct>size-16||!FirmwareResource.Match(image,root+wbct,"WBCT")||Read(image,root+wbct+8)!=1)
            throw new InvalidDataException("Menu WBCT association.");
        int scne=Read(image,root+wbct+12);
        if(scne<24||scne>size-24||!FirmwareResource.Match(image,root+scne,"SCNE"))throw new InvalidDataException("Menu SCNE extent.");
        int cdf=Read(image,root+scne+12);
        if(cdf<24||cdf>size-8||!FirmwareResource.Match(image,root+cdf,"C-DF"))throw new InvalidDataException("Menu C-DF extent.");
        int count=Read(image,root+cdf+4);
        if(count!=archive.System.Count||count>(size-cdf-8)/4)throw new InvalidDataException("Menu ID count.");
        int found=-1;
        for(int i=0;i<count;i++)
        {
            int record=Read(image,root+cdf+8+i*4);
            if(record<24||record>size-4)throw new InvalidDataException("Menu ID record extent.");
            if(Read(image,root+record)!=id)continue;
            if(found>=0)throw new InvalidDataException("Duplicate menu wave ID.");found=i;
        }
        if(found<0)throw new InvalidDataException("Missing menu instrument wave ID.");
        var wave=archive.System[found];
        if(wave.BankIndex!=0||wave.Codec!=0||image[root+wave.DescriptorOffset+2]!=48||!wave.HasLoop)
            throw new InvalidDataException("Menu instrument source differs from qualified profile.");
        return found;
    }
    internal static void Validate(SystemMenuSound sound)
    {if(sound<SystemMenuSound.TurnSide||sound>SystemMenuSound.PressStart)throw new ArgumentOutOfRangeException(nameof(sound));}
    internal bool TryGetInfo(SystemMenuSound sound,out SystemMenuSoundInfo? info)
    {info=effects[(int)sound];return info!=null;}
    internal byte[] Snapshot(SystemMenuSound sound,CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();var recipe=recipes[(int)sound];
        if(recipe==null)throw new NotSupportedException(UnsupportedReason);
        return recipe.Packet(sources,cancellation);
    }
    public void Dispose(){foreach(var source in sources)source?.Dispose();foreach(var recipe in recipes)recipe?.Release();RetainedBytes=0;}
}
