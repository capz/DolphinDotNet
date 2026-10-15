using System.Buffers.Binary;
using DolphinDotNet.Imaging;
namespace DolphinDotNet.Assets;

/// <summary>Offline GX RGBA8 packing; packages are little endian on every host.</summary>
public static class DrawingPackages
{
    public static byte[] Texture(RasterImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        int w=image.Width,h=image.Height;
        if(w>1024||h>1024)throw new ArgumentOutOfRangeException(nameof(image),"Maximum texture dimension is 1024.");
        byte[] data=new byte[32+((w+3)/4)*((h+3)/4)*64];
        Put(data,0,0x58544e44);Put(data,4,1);Put(data,8,6);Put(data,12,w);Put(data,16,h);Put(data,20,data.Length-32);
        int at=32;
        for(int y=0;y<h;y+=4)for(int x=0;x<w;x+=4)
        {
            for(int j=0;j<16;j++)
            {
                int px=x+j%4,py=y+j/4;var c=px<w&&py<h?image[px,py]:default;
                data[at+j*2]=c.A;data[at+j*2+1]=c.R;data[at+32+j*2]=c.G;data[at+33+j*2]=c.B;
            }
            at+=64;
        }
        return data;
    }
    public static byte[] Font(RasterImage atlas,int cellWidth,int cellHeight,int firstCharacter=32,int characters=96)
    {
        ArgumentNullException.ThrowIfNull(atlas);
        if(cellWidth<=0||cellHeight<=0||atlas.Width%cellWidth!=0||atlas.Height%cellHeight!=0||firstCharacter<0||characters<=0||firstCharacter>65535||characters>65536-firstCharacter||characters>(atlas.Width/cellWidth)*(atlas.Height/cellHeight))throw new ArgumentException("Invalid fixed-cell atlas or character range.");
        byte[] texture=Texture(atlas),data=new byte[32+texture.Length];
        Put(data,0,0x54464e44);Put(data,4,1);Put(data,8,cellWidth);Put(data,12,cellHeight);Put(data,16,firstCharacter);Put(data,20,characters);Put(data,24,texture.Length);texture.CopyTo(data,32);return data;
    }
    private static void Put(byte[] data,int offset,int value)=>BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset,4),value);
}
