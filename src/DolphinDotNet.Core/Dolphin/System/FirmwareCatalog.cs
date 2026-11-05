using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;

namespace Dolphin.System;

public enum FirmwareResourceKind { Unknown, Texture, Model }

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
        Kind=decoded.Length>=8&&Match(decoded,0,"J3D1bmd1")?FirmwareResourceKind.Model:FirmwareResourceKind.Unknown;
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
    private readonly FirmwareStereoAudio? startupAudio;
    private readonly FirmwareTextureInfo[]?[] textureCache;
    private readonly string[]?[] modelNamesCache;
    private readonly FirmwareWaveSystemInfo[] waveSystems;
    public int WaveSystemCount=>waveSystems.Length;
    public FirmwareWaveSystemInfo GetWaveSystem(int index)=>waveSystems[index];
    public FirmwareFileTable? Files {get;}
    public int Count=>resources.Length;
    public FirmwareResource this[int index]=>resources[index];
    private FirmwareCatalog(FirmwareResource[] metadata,byte[][] compressed,FirmwareFileTable? files,FirmwareStereoAudio? audio,FirmwareWaveSystemInfo[] waves)
    {resources=metadata;records=compressed;Files=files;startupAudio=audio;waveSystems=waves;textureCache=new FirmwareTextureInfo[metadata.Length][];modelNamesCache=new string[metadata.Length][];}
    public FirmwareStereoAudio GetStartupAudio()
    {
        if(records==null)throw new ObjectDisposedException(nameof(FirmwareCatalog));
        if(startupAudio==null)throw new NotSupportedException("No unique validated firmware WSYS/stereo archive layout.");
        return startupAudio;
    }
    internal static bool HasEvidence(byte[] data,CancellationToken cancellation)
    {
        try { using(var catalog=Scan(data,cancellation,false))return catalog.Count>=2; }
        catch(InvalidDataException){return false;}
    }
    internal static FirmwareCatalog Scan(byte[] data,CancellationToken cancellation,bool includeMetadata=true)
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
        var files=includeMetadata?FirmwareFileTable.Inspect(data,cancellation):null;
        var waves=includeMetadata?FirmwareWaveSystemInfo.Scan(data,cancellation):new FirmwareWaveSystemInfo[0];
        var audio=includeMetadata?FirmwareStereoAudio.Discover(data,files,waves,cancellation):null;
        if(audio!=null&&audio.FirmwareOffset>=0)
            foreach(var record in metadata)
                if(audio.FirmwareOffset<record.Offset+record.CompressedBytes&&record.Offset<audio.FirmwareOffset+audio.ArchiveBytes)
                {audio.Release();throw new InvalidDataException("Firmware audio archive overlaps a compressed resource.");}
        if(audio!=null&&audio.CompressedBytes>MaximumDecodedBytes-total)throw new InvalidDataException("Firmware catalog retention budget exceeded.");
        return new FirmwareCatalog(metadata.ToArray(),compressed.ToArray(),files,audio,waves);
    }
    public byte[] Extract(int index,CancellationToken cancellationToken=default)
    {
        var source=records;if(source==null)throw new ObjectDisposedException(nameof(FirmwareCatalog));
        if(index<0||index>=source.Length)throw new ArgumentOutOfRangeException(nameof(index));
        int used;return Decode(source[index],0,source[index].Length,out used,cancellationToken);
    }
    /// <summary>Inspect original J3D1 sections without claiming rendering support.</summary>
    public FirmwareModelInfo InspectModel(int index)=>FirmwareModelInfo.Inspect(Extract(index));
    public FirmwareTextureInfo[] GetTextures(int index)
    {
        if(records==null)throw new ObjectDisposedException(nameof(FirmwareCatalog));
        if(index<0||index>=Count)throw new ArgumentOutOfRangeException(nameof(index));
        var cached=textureCache[index];
        if(cached==null)
        {
            var decoded=Extract(index);
            if(resources[index].Kind==FirmwareResourceKind.Model)
                cached=FirmwareModelInfo.Inspect(decoded).ReadTextures(decoded,index);
            else
            {
                var bti=FirmwareTextures.Extract(decoded,0);
                cached=new[]{FirmwareTextureInfo.FromHeader(index,0,bti)};
            }
            textureCache[index]=cached;
        }
        // Cache immutable metadata, never model/ROM payloads or shared GX handles.
        var copy=new FirmwareTextureInfo[cached.Length];Array.Copy(cached,copy,copy.Length);return copy;
    }
    /// <summary>Identify original startup/menu models by bounded JNT1 names.
    /// Multiple matching resources fail; this does not replace native validation.</summary>
    public int GetModelIndex(SystemModel name)
    {
        if(records==null)throw new ObjectDisposedException(nameof(FirmwareCatalog));
        if(name<SystemModel.BaseCube||name>SystemModel.MemoryCube)throw new ArgumentOutOfRangeException(nameof(name));
        string root=name==SystemModel.BaseCube?"boot_demo_base_cube":name==SystemModel.CoverCube?"boot_demo_cover_cube":
            name==SystemModel.Logo?"boot_demo_mark":name==SystemModel.Wordmark?"root":name==SystemModel.SmallCube?"s_cube":
            name==SystemModel.MenuCube?"basecube":name==SystemModel.MenuIcon0?"i_cube0":name==SystemModel.MenuIcon1?"i_cube1":"cube_in_manu";
        int found=-1;
        for(int i=0;i<Count;i++)
        {
            if(resources[i].Kind!=FirmwareResourceKind.Model)continue;
            var names=modelNamesCache[i];
            if(names==null)
            {
                var data=Extract(i);
                try{names=FirmwareModelInfo.Inspect(data).ReadNodeNames(data);}
                catch(InvalidDataException){continue;}catch(NotSupportedException){continue;}
                modelNamesCache[i]=names;
            }
            if(names.Length==0||names[0]!=root)continue;
            if(name==SystemModel.Wordmark&&(names.Length<3||names[1]!="logotype_null"||names[2]!="nintendo_null"))continue;
            if(found>=0)throw new InvalidDataException("Ambiguous named firmware model.");found=i;
        }
        if(found<0)throw new NotSupportedException("No validated identity for requested firmware model.");
        return found;
    }
    /// <summary>Resolve a named intro texture. Published output sizes and header
    /// positions are only profile hints: J3D1 section structure, TEX1 membership,
    /// all BTI ranges and every logo face are independently checked. Multiple
    /// matching records are rejected. Unrecognized revisions fail explicitly.</summary>
    public FirmwareTextureInfo GetTexture(SystemTexture name)
    {
        if(records==null)throw new ObjectDisposedException(nameof(FirmwareCatalog));
        if(name<SystemTexture.Environment||name>SystemTexture.Wordmark)
            throw new ArgumentOutOfRangeException(nameof(name));
        int size=name==SystemTexture.Environment?0x6140:name==SystemTexture.Tiles?0x1020:
            name==SystemTexture.Wordmark?0x64a0:0x16f60;
        int header=name==SystemTexture.Environment?8448:name==SystemTexture.Tiles?0:
            name==SystemTexture.Wordmark?11616:name==SystemTexture.LogoTop?5056:
            name==SystemTexture.LogoLeft?5088:5120;
        FirmwareTextureInfo? found=null;
        for(int i=0;i<Count;i++)
        {
            if(resources[i].DecodedBytes!=size)continue;
            FirmwareTextureInfo[] textures;
            try{textures=GetTextures(i);}
            catch(InvalidDataException){continue;}
            catch(NotSupportedException){continue;}
            bool logo=name>=SystemTexture.LogoTop&&name<=SystemTexture.LogoRight;
            if(logo)
            {
                int faces=0;
                for(int j=0;j<textures.Length;j++)
                    if(textures[j].HeaderOffset==5056||textures[j].HeaderOffset==5088||textures[j].HeaderOffset==5120)faces++;
                if(faces!=3)continue;
            }
            for(int j=0;j<textures.Length;j++)if(textures[j].HeaderOffset==header)
            {
                if(found!=null)throw new InvalidDataException("Multiple validated candidates for named firmware texture.");
                found=textures[j];
            }
        }
        if(found==null)throw new NotSupportedException("No validated layout for requested firmware texture in this revision.");
        return found;
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
    public void Dispose(){var data=records;if(data==null)return;foreach(var record in data)Array.Clear(record,0,record.Length);startupAudio?.Release();Array.Clear(textureCache,0,textureCache.Length);Array.Clear(modelNamesCache,0,modelNamesCache.Length);records=null;}
}
