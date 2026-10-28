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
    /// <summary>v2 adds a complete power-of-two RGBA8 mip chain through 1x1.
    /// Filtering averages premultiplied RGBA to avoid transparent color fringes;
    /// RGB averaging is in byte/sRGB space, without gamma correction.</summary>
    public static byte[] Texture(RasterImage image,bool mipmaps)
    {
        ArgumentNullException.ThrowIfNull(image);
        if(!mipmaps)return Texture(image);
        if(image.Width>1024||image.Height>1024||(image.Width&(image.Width-1))!=0||(image.Height&(image.Height-1))!=0)
            throw new ArgumentException("Mipmapped dimensions must be powers of two, at most 1024.");
        using var payload=new MemoryStream();
        RasterImage current=image;int levels=0;
        try {
            while(true){
                var packed=Texture(current);payload.Write(packed,32,packed.Length-32);levels++;
                if(current.Width==1&&current.Height==1)break;
                var next=new RasterImage(Math.Max(1,current.Width/2),Math.Max(1,current.Height/2));
                for(int y=0;y<next.Height;y++)for(int x=0;x<next.Width;x++){
                    int a=0,r=0,g=0,b=0;
                    for(int dy=0;dy<2;dy++)for(int dx=0;dx<2;dx++){
                        var c=current[Math.Min(current.Width-1,x*2+dx),Math.Min(current.Height-1,y*2+dy)];
                        a+=c.A;r+=c.R*c.A;g+=c.G*c.A;b+=c.B*c.A;
                    }
                    next[x,y]=new Rgba32((byte)(a==0?0:(r+a/2)/a),(byte)(a==0?0:(g+a/2)/a),
                        (byte)(a==0?0:(b+a/2)/a),(byte)((a+2)/4));
                }
                if(current!=image)current.Dispose();current=next;
            }
        }finally{if(current!=image)current.Dispose();}
        byte[] data=new byte[32+payload.Length];
        Put(data,0,0x58544e44);Put(data,4,2);Put(data,8,6);Put(data,12,image.Width);Put(data,16,image.Height);
        Put(data,20,data.Length-32);Put(data,24,levels);payload.ToArray().CopyTo(data,32);return data;
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
