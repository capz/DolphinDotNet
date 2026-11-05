using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;

namespace Dolphin.System;

public enum FirmwareResourceKind { Unknown, Texture, UnsupportedModel }

/// <summary>Validated resource metadata, independent of the firmware image lifetime.
/// BMD1 recognition is metadata only, not a claim of rendering support.</summary>
public sealed class FirmwareResource
{
    public int Offset {get;}
    public int CompressedBytes {get;}
    public int DecodedBytes {get;}
    public FirmwareResourceKind Kind {get;}
    internal FirmwareResource(int offset,int compressed,byte[] decoded)
    {
        Offset=offset;CompressedBytes=compressed;DecodedBytes=decoded.Length;
        Kind=decoded.Length>=8&&Match(decoded,0,"J3D1bmd1")?FirmwareResourceKind.UnsupportedModel:FirmwareResourceKind.Unknown;
    }
    internal static bool Match(byte[] d,int at,string s)
    {if(at<0||at>d.Length-s.Length)return false;for(int i=0;i<s.Length;i++)if(d[at+i]!=s[i])return false;return true;}
}

/// <summary>Bounded Yay0 inventory. Retains compressed records only, not the ROM.
/// Unknown layouts remain unnamed; no assets are identified by output size alone.</summary>
public sealed class FirmwareCatalog : IDisposable
{
    public const int MaximumDecodedBytes=2*1024*1024;
    public const int MaximumResources=128;
    private readonly FirmwareResource[] resources;
    private byte[][]? records;
    public int Count=>resources.Length;
    public FirmwareResource this[int index]=>resources[index];
    private FirmwareCatalog(FirmwareResource[] metadata,byte[][] compressed){resources=metadata;records=compressed;}
    internal static bool HasEvidence(byte[] data,CancellationToken cancellation)
    {
        try { using(var catalog=Scan(data,cancellation))return catalog.Count>=2; }
        catch(InvalidDataException){return false;}
    }
    internal static FirmwareCatalog Scan(byte[] data,CancellationToken cancellation)
    {
        var metadata=new List<FirmwareResource>();var compressed=new List<byte[]>();
        int previousEnd=0,total=0,work=0;
        for(int at=0x100;at<=0x1aff00-16;at++)
        {
            if((at&4095)==0)cancellation.ThrowIfCancellationRequested();
            if(!FirmwareResource.Match(data,at,"Yay0"))continue;
            int used;byte[] decoded;
            int declared;
            try { declared=U32(data,at+4); } catch(InvalidDataException){continue;}
            if(declared<=0||declared>MaximumDecodedBytes)continue;
            if(declared>16*MaximumDecodedBytes-work)throw new InvalidDataException("Firmware decode work budget exceeded.");
            work+=declared;
            try { decoded=Decode(data,at,0x1aff00-at,out used,cancellation); }
            catch(InvalidDataException){continue;}
            if(at<previousEnd)throw new InvalidDataException("Overlapping Yay0 records.");
            if(metadata.Count==MaximumResources||used>MaximumDecodedBytes-total)
                throw new InvalidDataException("Firmware resource budget exceeded.");
            var record=new byte[used];Array.Copy(data,at,record,0,used);
            metadata.Add(new FirmwareResource(at,used,decoded));compressed.Add(record);
            previousEnd=at+used;total+=used;
        }
        return new FirmwareCatalog(metadata.ToArray(),compressed.ToArray());
    }
    public byte[] Extract(int index,CancellationToken cancellationToken=default)
    {
        var source=records;if(source==null)throw new ObjectDisposedException(nameof(FirmwareCatalog));
        if(index<0||index>=source.Length)throw new ArgumentOutOfRangeException(nameof(index));
        int used;return Decode(source[index],0,source[index].Length,out used,cancellationToken);
    }
    /// <summary>Decode a standalone Yay0 record with disjoint mask/link/literal streams.
    /// Rejects backreferences preceding output and runs crossing declared output size.</summary>
    public static byte[] Decompress(byte[] data,CancellationToken cancellationToken=default)
    {
        if(data==null)throw new ArgumentNullException(nameof(data));
        int used;return Decode(data,0,data.Length,out used,cancellationToken);
    }
    private static int U32(byte[] data,int at)
    {uint n=((uint)data[at]<<24)|((uint)data[at+1]<<16)|((uint)data[at+2]<<8)|data[at+3];if(n>int.MaxValue)throw new InvalidDataException("Yay0 integer overflow.");return (int)n;}
    private static byte[] Decode(byte[] data,int start,int available,out int used,CancellationToken cancellation)
    {
        used=0;
        if(available<16||!FirmwareResource.Match(data,start,"Yay0"))throw new InvalidDataException("Missing Yay0 header.");
        int size=U32(data,start+4),links=U32(data,start+8),literal=U32(data,start+12);
        if(size<=0||size>MaximumDecodedBytes||links<16||literal<links||literal>available)
            throw new InvalidDataException("Invalid Yay0 streams or output size.");
        int mask=16,linkEnd=literal,maskEnd=links,at=0,bits=0,steps=0;uint word=0;
        var output=new byte[size];
        while(at<size)
        {
            if((steps++&1023)==0)cancellation.ThrowIfCancellationRequested();
            if(bits==0)
            {
                if(mask>maskEnd-4)throw new InvalidDataException("Truncated Yay0 mask stream.");
                word=unchecked((uint)U32Bits(data,start+mask));mask+=4;bits=32;
            }
            bool copy=(word&0x80000000)!=0;word<<=1;bits--;
            if(copy)
            {if(literal>=available)throw new InvalidDataException("Truncated Yay0 literal.");output[at++]=data[start+literal++];}
            else
            {
                if(links>linkEnd-2)throw new InvalidDataException("Truncated Yay0 link.");
                int code=(data[start+links]<<8)|data[start+links+1];links+=2;
                int count=code>>12,distance=(code&4095)+1;
                if(count==0){if(literal>=available)throw new InvalidDataException("Truncated Yay0 run.");count=data[start+literal++]+18;}else count+=2;
                if(distance>at||count>size-at)throw new InvalidDataException("Invalid Yay0 backreference.");
                for(int i=0;i<count;i++){output[at]=output[at-distance];at++;}
            }
        }
        used=Math.Max(mask,Math.Max(links,literal));return output;
    }
    private static uint U32Bits(byte[] d,int a)=>((uint)d[a]<<24)|((uint)d[a+1]<<16)|((uint)d[a+2]<<8)|d[a+3];
    public void Dispose(){var data=records;if(data==null)return;foreach(var record in data)Array.Clear(record,0,record.Length);records=null;}
}
