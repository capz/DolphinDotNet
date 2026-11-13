using System;
using System.IO;
using System.Threading;

namespace Dolphin.System;

/// <summary>Encoding of the encrypted portion of an IPL firmware dump.</summary>
public enum FirmwareEncoding { Auto, Scrambled, Descrambled }
/// <summary>How the bytes were obtained. ConsoleRom does not claim physical
/// hardware: emulator ROM implementations use the same EXI interface.</summary>
public enum FirmwareSource { Memory, ExplicitFile, ConsoleRom }

/// <summary>Owned, read-only, exactly 2 MiB firmware image. Dispose as soon as
/// resources have been extracted. Never writes a copy to storage.</summary>
public sealed class FirmwareImage : IDisposable
{
    public const int ImageBytes = 0x200000;
    private byte[]? bytes;
    public string Revision { get; }
    public FirmwareEncoding Encoding { get; }
    public FirmwareSource Source { get; }
    private FirmwareImage(byte[] data, string revision, FirmwareEncoding encoding,FirmwareSource source)
    { bytes=data; Revision=revision; Encoding=encoding; Source=source; }

    /// <summary>Load an explicitly supplied IPL dump. No implicit filename search.
    /// Cancellation is observed between bounded reads and during descrambling.</summary>
    public static FirmwareImage Load(string path, FirmwareEncoding encoding=FirmwareEncoding.Auto,
        CancellationToken cancellationToken=default)
    {
        if(path==null)throw new ArgumentNullException(nameof(path));
        using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            if(stream.Length!=ImageBytes)throw new InvalidDataException("Firmware must be exactly 2 MiB.");
            var data=new byte[ImageBytes];
            try
            {
                int at=0;
                while(at<data.Length)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int n=stream.Read(data,at,Math.Min(16384,data.Length-at));
                    if(n==0)throw new EndOfStreamException();
                    at+=n;
                }
                return Take(data,encoding,cancellationToken,FirmwareSource.ExplicitFile);
            }
            catch { Array.Clear(data,0,data.Length); throw; }
        }
    }
    /// <summary>Copies caller memory before validation; caller retains ownership.</summary>
    public static FirmwareImage Read(byte[] data, FirmwareEncoding encoding=FirmwareEncoding.Auto,
        CancellationToken cancellationToken=default)
    {
        if(data==null)throw new ArgumentNullException(nameof(data));
        if(data.Length!=ImageBytes)throw new InvalidDataException("Firmware must be exactly 2 MiB.");
        var owned=(byte[])data.Clone();
        try { return Take(owned,encoding,cancellationToken); }
        catch { Array.Clear(owned,0,owned.Length); throw; }
    }
    internal static FirmwareImage Take(byte[] data,FirmwareEncoding encoding,CancellationToken cancellation,FirmwareSource source=FirmwareSource.Memory)
    {
        if(encoding<FirmwareEncoding.Auto||encoding>FirmwareEncoding.Descrambled)
            throw new ArgumentOutOfRangeException(nameof(encoding));
        // The unencrypted revision text must terminate inside the 0x100-byte header.
        int end=0;
        while(end<0x100&&data[end]!=0)
        { if((data[end]<32&&data[end]!=9&&data[end]!=10&&data[end]!=13)||data[end]>126)throw new InvalidDataException("Invalid firmware revision header."); end++; }
        if(end<8||end==0x100)throw new InvalidDataException("Missing firmware revision header.");
        string revision=global::System.Text.Encoding.ASCII.GetString(data,0,end);
        // Retail headers carry publisher/copyright text, with a revision suffix
        // on later images. Early images have no NTSC/PAL suffix. The descriptive
        // prefix is also accepted for original, generated test images; neither
        // header form substitutes for the independent encoding evidence below.
        bool retailHeader=revision.StartsWith("(C) ",StringComparison.Ordinal)&&
            revision.Contains("Nintendo.")&&revision.Contains("ArtX Inc.");
        if(!retailHeader&&!revision.StartsWith("Nintendo GameCube IPL",StringComparison.Ordinal))
            throw new NotSupportedException("Unrecognized firmware source.");
        cancellation.ThrowIfCancellationRequested();
        if(encoding==FirmwareEncoding.Scrambled)Transform(data,cancellation);
        else if(encoding==FirmwareEncoding.Auto)
        {
            // Evidence is multiple independent, fully decoded, non-overlapping records,
            // never just a signature or one of the known output sizes.
            bool plain=FirmwareCatalog.HasEvidence(data,cancellation);
            Transform(data,cancellation);
            bool encrypted=FirmwareCatalog.HasEvidence(data,cancellation);
            if(plain==encrypted)throw new InvalidDataException("Ambiguous or unsupported firmware encoding.");
            if(plain)Transform(data,cancellation);
            encoding=encrypted?FirmwareEncoding.Scrambled:FirmwareEncoding.Descrambled;
        }
        return new FirmwareImage(data,revision,encoding,source);
    }
    /// <summary>Apply the IPL stream cipher in place to the encrypted region only.
    /// Symmetric: applying twice restores the input. Header and font region are untouched.</summary>
    public static void Transform(byte[] data,CancellationToken cancellationToken=default)
    {
        if(data==null)throw new ArgumentNullException(nameof(data));
        if(data.Length!=ImageBytes)throw new InvalidDataException("Firmware must be exactly 2 MiB.");
        // IPL cipher reversed by Segher Boessenkool, copyright 2008.
        // Translated from GC-anim-Wii's MIT-licensed reference;
        // see docs/licenses/GC-anim-Wii-MIT.txt.
        int t=0x2953,u=0xd9c2,v=0x3ff1,x=1;
        for(int at=0x100;at<0x1aff00;at++)
        {
            if((at&4095)==0)cancellationToken.ThrowIfCancellationRequested();
            int acc=0;
            for(int bit=0;bit<8;bit++)
            {
                int t0=t&1,t1=(t>>1)&1,u0=u&1,u1=(u>>1)&1,v0=v&1;
                x^=t1^v0^(u0|u1)^((t0^u1^v0)&(t0^u0));
                if(t0==u0){v>>=1;if(v0!=0)v^=0xb3d0;}
                if(t0==0){u>>=1;if(u0!=0)u^=0xfb10;}
                t>>=1;if(t0!=0)t^=0xa740;
                acc=(acc<<1)|x;
            }
            data[at]^=(byte)acc;
        }
    }
    internal byte[] Data { get { if(bytes==null)throw new ObjectDisposedException(nameof(FirmwareImage)); return bytes; } }
    public FirmwareFileTable? InspectFileTable(CancellationToken cancellationToken=default)=>FirmwareFileTable.Inspect(Data,cancellationToken);
    public FirmwareCatalog CreateCatalog(CancellationToken cancellationToken=default)=>CreateCatalog(FirmwareCatalogOptions.None,cancellationToken);
    /// <summary>Request additional retention while this image is alive. Both
    /// arguments are required so legacy CreateCatalog(default) stays unambiguous.</summary>
    public FirmwareCatalog CreateCatalog(FirmwareCatalogOptions options,CancellationToken cancellationToken)
    {
        if((options&~FirmwareCatalogOptions.IncludeMenuAudio)!=0)throw new ArgumentOutOfRangeException(nameof(options));
        return FirmwareCatalog.Scan(Data,cancellationToken,true,options);
    }
    public byte[] ReadRange(int offset,int count)
    {
        var data=Data;
        if(offset<0||count<0||offset>data.Length||count>data.Length-offset)throw new ArgumentOutOfRangeException(nameof(count));
        var result=new byte[count];Array.Copy(data,offset,result,0,count);return result;
    }
    public void Dispose(){if(bytes==null)return;Array.Clear(bytes,0,bytes.Length);bytes=null;}
}
