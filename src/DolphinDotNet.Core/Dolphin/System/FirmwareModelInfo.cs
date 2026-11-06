using System;
using System.Collections.Generic;
using System.IO;
using Dolphin.Formats;
using static Dolphin.Formats.GameCubeBinary;

namespace Dolphin.System;

/// <summary>A bounded J3D1 section. Section recognition does not imply that its
/// vertex/material semantics are supported by the renderer.</summary>
public sealed class FirmwareSection
{
    public string Tag {get;}
    public int Offset {get;}
    public int Length {get;}
    internal FirmwareSection(string tag,int offset,int length){Tag=tag;Offset=offset;Length=length;}
}

/// <summary>Structural inspection of original IPL J3D1bmd1 resources. No source
/// display lists are executed. Geometry/material rendering is a separate capability.</summary>
public sealed class FirmwareModelInfo
{
    private readonly FirmwareSection[] sections;
    public int DeclaredBytes {get;}
    public int SectionCount=>sections.Length;
    public FirmwareSection this[int index]=>sections[index];
    private FirmwareModelInfo(int length,FirmwareSection[] chunks){DeclaredBytes=length;sections=chunks;}
    public static FirmwareModelInfo Inspect(byte[] data)
    {
        Check(data,0,32);
        if(data.Length>FirmwareCatalog.MaximumDecodedBytes||!FirmwareResource.Match(data,0,"J3D1bmd1"))
            throw new NotSupportedException("Expected an IPL J3D1bmd1 model.");
        int length=AssetInt(U32(data,8)),count=AssetInt(U32(data,12));
        if(length<32||length>data.Length||count<1||count>64)
            throw new InvalidDataException("Invalid J3D1 file length or section count.");
        var chunks=new FirmwareSection[count];int offset=32;
        for(int i=0;i<count;i++)
        {
            // Some original J3D1 files retain a stale, smaller file-size field.
            // The Yay0 output is the enclosing extent: every bounded section
            // must cover it exactly, regardless of that informational field.
            if(offset>data.Length-8)throw new InvalidDataException("Truncated J3D1 section header.");
            int size=AssetInt(U32(data,offset+4));
            if(size<8||size>data.Length-offset)throw new InvalidDataException("J3D1 section exceeds decoded model.");
            for(int j=0;j<4;j++)if(data[offset+j]<32||data[offset+j]>126)
                throw new InvalidDataException("Invalid J3D1 section identifier.");
            string tag=Ascii(data,offset,4);
            for(int j=0;j<i;j++)if(chunks[j].Tag==tag)
                throw new InvalidDataException("Ambiguous duplicate J3D1 section.");
            chunks[i]=new FirmwareSection(tag,offset,size);offset+=size;
        }
        if(offset!=data.Length)throw new InvalidDataException("J3D1 sections do not cover the decoded record.");
        return new FirmwareModelInfo(length,chunks);
    }
    /// <summary>Read a structurally valid TEX1 header table. Every BTI is validated
    /// relative to its own header and bounded by its containing section. Unknown
    /// TEX layouts fail explicitly; no arbitrary texture-header scanning occurs.</summary>
    public FirmwareTextureInfo[] ReadTextures(byte[] data,int resourceIndex=0)
    {
        // Prevent metadata from one model being used to bypass another's bounds.
        var checkedModel=Inspect(data);
        if(checkedModel.DeclaredBytes!=DeclaredBytes||checkedModel.SectionCount!=SectionCount)
            throw new InvalidDataException("Model metadata does not match resource.");
        FirmwareSection? texture=null;
        for(int i=0;i<checkedModel.SectionCount;i++)if(checkedModel[i].Tag=="TEX1")texture=checkedModel[i];
        if(texture==null)throw new NotSupportedException("Model has no recognized texture section.");
        if(texture.Length<20)throw new InvalidDataException("Truncated TEX1 header.");
        int count=U16(data,texture.Offset+8),table=AssetInt(U32(data,texture.Offset+12));
        if(count<1||count>256||table<20||table>texture.Length||count>(texture.Length-table)/32)
            throw new InvalidDataException("Invalid TEX1 BTI table.");
        long texels=0;
        for(int i=0;i<count;i++)
        {
            int at=texture.Offset+table+i*32;
            int width=U16(data,at+2),height=U16(data,at+4),mips=Math.Max(1,(int)data[at+24]);
            if(width<1||height<1||width>1024||height>1024||mips>11)
                throw new InvalidDataException("Invalid TEX1 image dimensions or mips.");
            for(int level=0;level<mips;level++)
            {texels+=(long)width*height;width=Math.Max(1,width/2);height=Math.Max(1,height/2);}
            if(texels>2L*FirmwareCatalog.MaximumDecodedBytes)
                throw new InvalidDataException("TEX1 decoding work budget exceeded.");
        }
        var section=new byte[texture.Length];Array.Copy(data,texture.Offset,section,0,section.Length);
        var result=new FirmwareTextureInfo[count];
        for(int i=0;i<count;i++)
        {
            int at=table+i*32;
            // Extract copies only actual texture/palette bytes and validates all mips.
            var bti=FirmwareTextures.Extract(section,at);
            result[i]=FirmwareTextureInfo.FromHeader(resourceIndex,texture.Offset+at,bti);
        }
        return result;
    }
    /// <summary>Read bounded joint names/remaps for resource identification.
    /// Geometry and material validation still occurs in the native importer.</summary>
    public string[] ReadNodeNames(byte[] data)
    {
        var model=Inspect(data);FirmwareSection? joints=null;
        for(int i=0;i<model.SectionCount;i++)if(model[i].Tag=="JNT1")joints=model[i];
        if(joints==null)throw new NotSupportedException("Model has no JNT1 section.");
        if(joints.Length<24)throw new InvalidDataException("Truncated JNT1 header.");
        int at=joints.Offset,count=U16(data,at+8),table=AssetInt(U32(data,at+12)),remap=AssetInt(U32(data,at+16)),names=AssetInt(U32(data,at+20));
        if(count<1||count>1024||table<24||table>joints.Length||remap<24||remap>joints.Length-count*2||
           names<24||names>joints.Length-4-count*4||U16(data,at+names)!=count)
            throw new InvalidDataException("Invalid JNT1 tables.");
        var result=new string[count];
        for(int i=0;i<count;i++)
        {
            int index=U16(data,at+remap+i*2);
            if(index>=count||index>(joints.Length-table)/64||table+index*64>joints.Length-64)
                throw new InvalidDataException("JNT1 joint outside section.");
            int offset=U16(data,at+names+6+i*4);
            if(offset<4+count*4||offset>=joints.Length-names)throw new InvalidDataException("JNT1 name outside table.");
            int first=at+names+offset,end=first;
            while(end<at+joints.Length&&end-first<64&&data[end]!=0)
            {if(data[end]<32||data[end]>126)throw new InvalidDataException("Unsupported JNT1 name encoding.");end++;}
            if(end==first||end==at+joints.Length||end-first==64)throw new InvalidDataException("Invalid JNT1 name.");
            result[i]=Ascii(data,first,end-first);
        }
        return result;
    }
}

/// <summary>Validated texture metadata referring to a decompressed catalog record.</summary>
public sealed class FirmwareTextureInfo
{
    public int ResourceIndex {get;}
    public int HeaderOffset {get;}
    public int Width {get;}
    public int Height {get;}
    public int ImageFormat {get;}
    public int MipCount {get;}
    private FirmwareTextureInfo(int resource,int header,int width,int height,int format,int mips)
    {ResourceIndex=resource;HeaderOffset=header;Width=width;Height=height;ImageFormat=format;MipCount=mips;}
    internal static FirmwareTextureInfo FromHeader(int resource,int header,byte[] bti)
        =>new FirmwareTextureInfo(resource,header,U16(bti,2),U16(bti,4),bti[0],Math.Max(1,(int)bti[24]));
}

public enum SystemTexture { Environment, Tiles, LogoTop, LogoLeft, LogoRight, Wordmark }
public enum SystemModel { BaseCube, CoverCube, Logo, Wordmark, SmallCube, MenuCube, MenuIcon0, MenuIcon1, MemoryCube }
