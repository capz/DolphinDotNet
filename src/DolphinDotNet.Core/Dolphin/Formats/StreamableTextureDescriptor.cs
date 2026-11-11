using System;
using System.IO;

namespace Dolphin.Formats;

[Flags]
public enum StreamableTextureFlags { None=0, MipChain=1, SparseLevels=2, QualityPair=4 }

/// <summary>Static source-normalized UV mapping for one independently stored image.</summary>
public readonly struct TextureLodUv
{
    public readonly float ScaleU,ScaleV,OffsetU,OffsetV;
    public TextureLodUv(float scaleU,float scaleV,float offsetU,float offsetV)
    {ScaleU=scaleU;ScaleV=scaleV;OffsetU=offsetU;OffsetV=offsetV;}
    public static TextureLodUv Identity=>new TextureLodUv(1,1,0,0);
}

public readonly struct StreamableTextureLevel
{
    public readonly int LogicalLod,Width,Height,Format,Offset,Length;
    public readonly uint Crc32;
    public readonly TextureLodUv Uv;
    internal StreamableTextureLevel(int lod,int width,int height,int format,int offset,int length,uint crc,TextureLodUv uv)
    {LogicalLod=lod;Width=width;Height=height;Format=format;Offset=offset;Length=length;Crc32=crc;Uv=uv;}
}

/// <summary>Immutable, header/index-only DNLT v1 descriptor. Reading this type never
/// reads a payload or allocates GPU resources. DNTX v1/v2 are separate formats.</summary>
public sealed class StreamableTextureDescriptor
{
    public const int HeaderBytes=64,EntryBytes=32,MaximumLevels=11,MaximumFileBytes=16*1024*1024;
    private readonly StreamableTextureLevel[] levels;
    public int Width {get;}
    public int Height {get;}
    public int DefaultFormat {get;}
    public int FileLength {get;}
    public int MetadataBytes {get;}
    public int CoarseIndex=>levels.Length-1;
    public int Count=>levels.Length;
    public StreamableTextureFlags Flags {get;}
    public bool IsQualityPair=>(Flags&StreamableTextureFlags.QualityPair)!=0;
    private StreamableTextureDescriptor(int width,int height,int format,int fileLength,int metadataBytes,StreamableTextureFlags flags,StreamableTextureLevel[] levels)
    {Width=width;Height=height;DefaultFormat=format;FileLength=fileLength;MetadataBytes=metadataBytes;Flags=flags;this.levels=levels;}
    public StreamableTextureLevel GetLevel(int index)
    {if(index<0||index>=levels.Length)throw new ArgumentOutOfRangeException("index");return levels[index];}
    private static void Need(bool valid,string reason){if(!valid)throw new InvalidDataException("DNLT: "+reason);}
    private static uint U32(byte[] b,int at)
    {return (uint)(b[at]|b[at+1]<<8|b[at+2]<<16|b[at+3]<<24);}
    private static int Int(byte[] b,int at)
    {uint value=U32(b,at);Need(value<=int.MaxValue,"integer overflow");return (int)value;}
    private static int U16(byte[] b,int at)=>b[at]|b[at+1]<<8;
    private static float Float(byte[] b,int at)=>Dolphin.Numerics.BitConverter.Int32BitsToSingle((int)U32(b,at));
    private static bool Finite(float x)=>!float.IsNaN(x)&&!float.IsInfinity(x);
    public static uint ComputeCrc32(byte[] bytes,int offset,int count,uint initial=0)
    {
        if(bytes==null)throw new ArgumentNullException("bytes");
        if(offset<0||count<0||offset>bytes.Length||count>bytes.Length-offset)throw new ArgumentOutOfRangeException("count");
        uint crc=~initial;for(int i=offset;i<offset+count;i++){crc^=bytes[i];for(int bit=0;bit<8;bit++)crc=(crc>>1)^((crc&1)!=0?0xedb88320u:0);}return ~crc;
    }
    public static int NativeByteCount(int width,int height,int format)
    {
        Need(width>0&&height>0&&width<=1024&&height<=1024,"native dimensions");
        Need(format==6||format==14,"unsupported GX format");
        return format==6?((width+3)/4)*((height+3)/4)*64:((width+7)/8)*((height+7)/8)*32;
    }
    /// <summary>Validate the fixed header before issuing the bounded metadata read.</summary>
    public static int GetMetadataBytes(byte[] header,int actualFileLength=-1)
    {
        if(header==null)throw new ArgumentNullException("header");Need(header.Length>=HeaderBytes,"truncated header");
        Need(U32(header,0)==0x544c4e44&&Int(header,4)==1,"magic/version");
        int flags=Int(header,8),count=Int(header,24),uv=Int(header,48),uvBytes=Int(header,52),length=Int(header,40);
        Need((flags==0||flags==1||flags==3||flags==4)&&count>0&&count<=MaximumLevels,"flags/level count");
        Need(flags!=4||count==2,"quality pair count");Need(flags!=0||count==1,"single-image count");
        int tableEnd=HeaderBytes+count*EntryBytes;
        Need(Int(header,32)==HeaderBytes&&Int(header,28)==count-1,"index/coarse entry");
        Need((uv==0&&uvBytes==0)||(uv==tableEnd&&uvBytes==count*16),"UV table range");
        int metadata=tableEnd+uvBytes,data=(metadata+31)&~31;
        Need(Int(header,36)==data&&length>=data&&length<=MaximumFileBytes&&(length&31)==0,"data/file bounds");
        Need(actualFileLength<0||length==actualFileLength,"declared file length");
        Need(U32(header,56)==0&&U32(header,60)==0,"reserved header fields");
        int width=Int(header,12),height=Int(header,16);NativeByteCount(width,height,Int(header,20));
        if((flags&1)!=0)Need((width&(width-1))==0&&(height&(height-1))==0,"mip logical dimensions must be powers of two");
        return metadata;
    }
    public static StreamableTextureDescriptor Read(byte[] prefix,int actualFileLength=-1)
    {
        int metadata=GetMetadataBytes(prefix,actualFileLength);Need(prefix.Length>=metadata,"truncated metadata");
        int count=Int(prefix,24),width=Int(prefix,12),height=Int(prefix,16),uvOffset=Int(prefix,48),flags=Int(prefix,8);
        Need(ComputeCrc32(prefix,HeaderBytes,metadata-HeaderBytes)==U32(prefix,44),"metadata checksum");
        int maxLod=0,largest=Math.Max(width,height);while(largest>1){maxLod++;largest/=2;}
        var levels=new StreamableTextureLevel[count];int previous=-1;
        for(int i=0;i<count;i++){
            int at=HeaderBytes+i*EntryBytes,lod=Int(prefix,at),w=U16(prefix,at+4),h=U16(prefix,at+6),format=Int(prefix,at+8),offset=Int(prefix,at+16),length=Int(prefix,at+20);
            Need(lod>previous&&lod<MaximumLevels,"unordered/duplicate logical level");previous=lod;
            Need(U32(prefix,at+12)==0&&U32(prefix,at+28)==0,"entry flags/reserved fields");
            Need(length==NativeByteCount(w,h,format)&&(offset&31)==0,"native size/alignment");
            Need(offset>=Int(prefix,36)&&offset<=Int(prefix,40)&&length<=Int(prefix,40)-offset,"payload bounds");
            var uv=uvOffset==0?TextureLodUv.Identity:new TextureLodUv(Float(prefix,uvOffset+i*16),Float(prefix,uvOffset+i*16+4),Float(prefix,uvOffset+i*16+8),Float(prefix,uvOffset+i*16+12));
            Need(Finite(uv.ScaleU)&&Finite(uv.ScaleV)&&Finite(uv.OffsetU)&&Finite(uv.OffsetV),"nonfinite UV transform");
            Need(uv.ScaleU>0&&uv.ScaleV>0&&uv.OffsetU>=0&&uv.OffsetV>=0&&uv.ScaleU+uv.OffsetU<=1.00001f&&uv.ScaleV+uv.OffsetV<=1.00001f,"UV usable rectangle");
            if((flags&1)!=0){
                Need(lod<=maxLod,"logical mip exponent");
                Need(Math.Abs(w*uv.ScaleU-Math.Max(1,width>>lod))<.02f&&Math.Abs(h*uv.ScaleV-Math.Max(1,height>>lod))<.02f,"mip half-size progression");
            }else Need(lod==i&&(i!=0||(Math.Abs(w*uv.ScaleU-width)<.02f&&Math.Abs(h*uv.ScaleV-height)<.02f)),"HIGH/LOW quality pair");
            levels[i]=new StreamableTextureLevel(lod,w,h,format,offset,length,U32(prefix,at+24),uv);
        }
        if(flags==1){Need(count==maxLod+1,"incomplete full mip chain");for(int i=0;i<count;i++)Need(levels[i].LogicalLod==i,"missing full-chain mip");}
        int cursor=Int(prefix,36);for(int i=count-1;i>=0;i--){Need(levels[i].Offset==cursor,"coarse-first payload order or overlap");cursor+=levels[i].Length;}
        Need(cursor==Int(prefix,40),"trailing or missing payload bytes");
        return new StreamableTextureDescriptor(width,height,Int(prefix,20),cursor,metadata,(StreamableTextureFlags)flags,levels);
    }
    /// <summary>Check a complete payload, or pass chunk results incrementally to
    /// ComputeCrc32 and compare with GetLevel(index).Crc32 before publication.</summary>
    public bool ValidatePayload(int index,byte[] bytes,int offset=0)
    {var level=GetLevel(index);return ComputeCrc32(bytes,offset,level.Length)==level.Crc32;}
}
