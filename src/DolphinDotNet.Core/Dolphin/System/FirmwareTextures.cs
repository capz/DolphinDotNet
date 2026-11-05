using System;
using System.IO;
using Dolphin.Formats;

namespace Dolphin.System;

/// <summary>Validated extraction at an explicitly identified BTI header. Offsets
/// are relative to that individual header, including shared/overlapping headers.</summary>
public static class FirmwareTextures
{
    public static byte[] Extract(byte[] resource,int headerOffset)
    {
        if(resource==null)throw new ArgumentNullException(nameof(resource));
        if(headerOffset<0||headerOffset>resource.Length-32)throw new ArgumentOutOfRangeException(nameof(headerOffset));
        int width=U16(resource,headerOffset+2),height=U16(resource,headerOffset+4);
        int fmt=resource[headerOffset],mips=Math.Max(1,(int)resource[headerOffset+24]);
        if(width<1||width>1024||height<1||height>1024||mips>11)
            throw new InvalidDataException("Invalid firmware BTI dimensions or mip count.");
        int tw,th,tb;
        switch(fmt)
        {
            case 0:case 8:case 14:tw=8;th=8;tb=32;break;
            case 1:case 2:case 9:tw=8;th=4;tb=32;break;
            case 3:case 4:case 5:case 10:tw=4;th=4;tb=32;break;
            case 6:tw=4;th=4;tb=64;break;
            default:throw new NotSupportedException("Unsupported firmware BTI format.");
        }
        if(resource[headerOffset+6]>2||resource[headerOffset+7]>2||resource[headerOffset+20]>5||resource[headerOffset+21]>1)
            throw new InvalidDataException("Unsupported firmware BTI sampler.");
        int pixels=0;
        for(int level=0;level<mips;level++)
        {
            pixels+=((width+tw-1)/tw)*((height+th-1)/th)*tb;
            if(level+1<mips&&width==1&&height==1)throw new InvalidDataException("Excess BTI mips.");
            width=Math.Max(1,width/2);height=Math.Max(1,height/2);
        }
        int image=Offset(resource,headerOffset+28);
        Range(resource,headerOffset,image,pixels);
        int palette=0,paletteBytes=0;
        if(fmt==8||fmt==9||fmt==10)
        {
            int count=U16(resource,headerOffset+10);
            if(resource[headerOffset+9]>2||count<1||count>(fmt==8?16:fmt==9?256:16384))
                throw new InvalidDataException("Invalid firmware BTI palette.");
            paletteBytes=count*2;palette=Offset(resource,headerOffset+12);
            Range(resource,headerOffset,palette,paletteBytes);
        }
        var result=new byte[32+paletteBytes+pixels];
        Array.Copy(resource,headerOffset,result,0,32);
        if(paletteBytes!=0)Array.Copy(resource,headerOffset+palette,result,32,paletteBytes);
        Array.Copy(resource,headerOffset+image,result,32+paletteBytes,pixels);
        Write(result,12,paletteBytes==0?0:32);Write(result,28,32+paletteBytes);
        // Reuse the production decoder and mip package validation.
        BtiTexture.CreateNativePackage(result);
        return result;
    }
    private static int U16(byte[] d,int a)=>(d[a]<<8)|d[a+1];
    private static int Offset(byte[] d,int a)
    {uint n=((uint)d[a]<<24)|((uint)d[a+1]<<16)|((uint)d[a+2]<<8)|d[a+3];if(n>int.MaxValue)throw new InvalidDataException("BTI offset overflow.");return (int)n;}
    private static void Range(byte[] d,int header,int offset,int count)
    {if(offset<32||offset>d.Length-header||count>d.Length-header-offset)throw new InvalidDataException("BTI payload outside resource.");}
    private static void Write(byte[] d,int a,int n){d[a]=(byte)(n>>24);d[a+1]=(byte)(n>>16);d[a+2]=(byte)(n>>8);d[a+3]=(byte)n;}
}
