using System;
using System.IO;
using Dolphin.Graphics;

/// <summary>
/// Direct native GX payload path for the GameCube fixture: avoids allocating
/// RGBA32 decoded copies or reaching the desktop-oriented BTI decoder through
/// the closed-world AOT compiler. This is not CI/TLUT palette support.
/// </summary>
internal static class NativeBtiPreview
{
    public static Texture2D Create(byte[] bytes)
    {
        if(bytes==null||bytes.Length<0x20)throw new InvalidDataException("Short BTI header.");
        int format=bytes[0];
        int width=(bytes[2]<<8)|bytes[3],height=(bytes[4]<<8)|bytes[5];
        if(width<4||height<4||width>128||height>128||
           (width&(width-1))!=0 || (height&(height-1))!=0)
            throw new NotSupportedException("Fixture supports 4..128 power-of-two BTI previews.");
        if(format!=0&&format!=1&&format!=2&&format!=3&&
           format!=4&&format!=5&&format!=6&&format!=14)
            throw new NotSupportedException("Native GX fixture has no CI indexed-palette residency.");
        int start=(int)((uint)bytes[0x1c]<<24|(uint)bytes[0x1d]<<16|
                        (uint)bytes[0x1e]<<8|bytes[0x1f]);
        if(start<0x20 || start>bytes.Length)
            throw new InvalidDataException("Invalid BTI native data offset.");
        var texture=Texture2D.CreateNative(width,height,(NativeTextureFormat)format,linear:false);
        try
        {
            if(texture.NativeBytes>bytes.Length-start)
                throw new InvalidDataException("Truncated BTI GX-tiled base level.");
            texture.ReplaceNative(bytes,start);
            int wrapS=bytes[6],wrapT=bytes[7];
            int min=bytes[0x14],mag=bytes[0x15];
            if(wrapS>2||wrapT>2||min>5||mag>1)
                throw new NotSupportedException("Unsupported BTI sampler profile.");
            // The direct fixture uploads the base level only; downgrade
            // authored mip filters to a defined non-mip fallback.
            if(min>=2)min&=1;
            short bias=(short)((bytes[0x1a]<<8)|bytes[0x1b]);
            float lod=bias/100f;
            if(lod< -4)lod=-4;if(lod>3.99f)lod=3.99f;
            texture.ConfigureSampler((TextureWrapMode)wrapS,(TextureWrapMode)wrapT,
                (TextureMinFilter)min,(TextureMagFilter)mag,lod);
            return texture;
        }
        catch
        {
            texture.Dispose();
            throw;
        }
    }
}
