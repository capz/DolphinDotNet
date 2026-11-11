using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Dolphin.Formats;
using DolphinDotNet.Imaging;

namespace DolphinDotNet.Assets;

public enum StreamableTextureFormat { Auto, Rgba8, Cmpr }
public sealed class StreamableTextureOptions
{
    public bool Mipmaps {get;set;}
    public int MinimumSize {get;set;}=1;
    public int[]? LogicalLevels {get;set;}
    public int Gutter {get;set;}
    public StreamableTextureFormat Format {get;set;}=StreamableTextureFormat.Auto;
}
public sealed class StreamableTextureAsset
{
    public byte[] Bytes {get;}
    public string Manifest {get;}
    internal StreamableTextureAsset(byte[] bytes,string manifest){Bytes=bytes;Manifest=manifest;}
}

/// <summary>Deterministic independent GX image levels. DNTX authoring is unchanged.</summary>
public static class StreamableTextures
{
    private sealed record Level(int Lod,int Width,int Height,byte[] Bytes,TextureLodUv Uv);
    private static void Put(byte[] bytes,int at,int value)=>BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at,4),value);
    private static void Float(byte[] bytes,int at,float value)=>Put(bytes,at,BitConverter.SingleToInt32Bits(value));
    private static bool PowerOfTwo(int x)=>x>0&&(x&(x-1))==0;
    private static void Dimensions(RasterImage image)
    {if(image.Width>1024||image.Height>1024)throw new ArgumentException("Streamable textures are limited to 1024 texels per axis.");}
    private static bool SmoothAlpha(RasterImage image)
    {for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++){byte a=image[x,y].A;if(a!=0&&a!=255)return true;}return false;}
    private static int Opaque(RasterImage image)
    {int count=0;for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++)if(image[x,y].A>=128)count++;return count;}
    private static string Hash(RasterImage image)
    {
        byte[] pixels=new byte[image.Width*image.Height*4];int at=0;
        for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++){var p=image[x,y];pixels[at++]=p.R;pixels[at++]=p.G;pixels[at++]=p.B;pixels[at++]=p.A;}
        return Convert.ToHexString(SHA256.HashData(pixels)).ToLowerInvariant();
    }
    private static RasterImage Half(RasterImage source)
    {
        var result=new RasterImage(Math.Max(1,source.Width/2),Math.Max(1,source.Height/2));
        for(int y=0;y<result.Height;y++)for(int x=0;x<result.Width;x++){
            int a=0,r=0,g=0,b=0;
            for(int dy=0;dy<2;dy++)for(int dx=0;dx<2;dx++){
                var p=source[Math.Min(source.Width-1,x*2+dx),Math.Min(source.Height-1,y*2+dy)];a+=p.A;r+=p.R*p.A;g+=p.G*p.A;b+=p.B*p.A;
            }
            result[x,y]=new Rgba32((byte)(a==0?0:(r+a/2)/a),(byte)(a==0?0:(g+a/2)/a),(byte)(a==0?0:(b+a/2)/a),(byte)((a+2)/4));
        }
        return result;
    }
    // A CMPR alpha mip keeps the closest representable number of covered texels.
    // Stable raster-order ties make output independent of thread count/platform.
    private static void Coverage(RasterImage image,int sourceCovered,int sourcePixels)
    {
        int wanted=(int)(((long)sourceCovered*image.Width*image.Height+sourcePixels/2)/sourcePixels),threshold=255,above=0;
        int[] histogram=new int[256];for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++)histogram[image[x,y].A]++;
        while(threshold>0&&above+histogram[threshold]<wanted){above+=histogram[threshold];threshold--;}
        int ties=wanted-above;
        for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++){
            var p=image[x,y];bool opaque=p.A>threshold||(p.A==threshold&&ties-->0);
            image[x,y]=new Rgba32(p.R,p.G,p.B,opaque?(byte)255:(byte)0);
        }
    }
    public static StreamableTextureAsset Encode(RasterImage high,StreamableTextureOptions? options=null,RasterImage? low=null)
    {
        ArgumentNullException.ThrowIfNull(high);options??=new StreamableTextureOptions();Dimensions(high);if(low!=null)Dimensions(low);
        if(options.Gutter<0||options.Gutter>16||!PowerOfTwo(options.MinimumSize)||options.MinimumSize>1024||!Enum.IsDefined(options.Format))throw new ArgumentException("Invalid streamable texture options.");
        if(low!=null&&(options.Mipmaps||options.LogicalLevels!=null||options.MinimumSize!=1))throw new ArgumentException("Quality pairs do not synthesize intermediate mips.");
        if(options.Mipmaps&&(!PowerOfTwo(high.Width)||!PowerOfTwo(high.Height)))throw new ArgumentException("Mip dimensions must be powers of two.");
        if(!options.Mipmaps&&(options.LogicalLevels!=null||options.MinimumSize!=1))throw new ArgumentException("Mip selection requires --mipmaps.");
        bool smooth=SmoothAlpha(high)||(low!=null&&SmoothAlpha(low));
        if(smooth&&options.Format==StreamableTextureFormat.Cmpr)throw new ArgumentException("CMPR cannot preserve smooth alpha; choose RGBA8 or auto.");
        int format=options.Format==StreamableTextureFormat.Rgba8||smooth?6:14;
        var levels=new List<Level>();int flags=low!=null?4:options.Mipmaps?1:0;
        int fullCount=1,edge=Math.Max(high.Width,high.Height);while(edge>1){edge/=2;fullCount++;}
        if(options.MinimumSize>Math.Max(high.Width,high.Height))throw new ArgumentException("Minimum size exceeds the source.");
        int[]? selected=options.LogicalLevels;
        if(selected!=null){if(selected.Length==0||selected.Distinct().Count()!=selected.Length||selected.Any(l=>l<0||l>=fullCount))throw new ArgumentException("Invalid sparse logical levels.");selected=selected.OrderBy(l=>l).ToArray();}
        RasterImage current=high;int lod=0,covered=Opaque(high),sourcePixels=high.Width*high.Height;
        try{
            while(true){
                if(selected==null||Array.IndexOf(selected,lod)>=0)levels.Add(Pack(current,lod,format,options.Gutter));
                if(low!=null){levels.Add(Pack(low,1,format,options.Gutter));break;}
                if(!options.Mipmaps||current.Width==1&&current.Height==1||Math.Max(current.Width,current.Height)<=options.MinimumSize)break;
                var next=Half(current);if(format==14)Coverage(next,covered,sourcePixels);
                if(current!=high)current.Dispose();current=next;lod++;
            }
        }finally{if(current!=high)current.Dispose();}
        if(levels.Count==0||selected!=null&&levels.Count!=selected.Length)throw new ArgumentException("Sparse levels lie below the minimum-size cutoff.");
        if(options.Mipmaps&&(levels.Count!=fullCount||levels.Where((level,index)=>level.Lod!=index).Any()))flags|=2;
        int metadata=64+levels.Count*32+(options.Gutter==0?0:levels.Count*16),dataOffset=(metadata+31)&~31;
        int total=dataOffset+levels.Sum(l=>l.Bytes.Length);byte[] bytes=new byte[total];
        Put(bytes,0,0x544c4e44);Put(bytes,4,1);Put(bytes,8,flags);Put(bytes,12,high.Width);Put(bytes,16,high.Height);Put(bytes,20,format);Put(bytes,24,levels.Count);Put(bytes,28,levels.Count-1);Put(bytes,32,64);Put(bytes,36,dataOffset);Put(bytes,40,total);
        int uvOffset=options.Gutter==0?0:64+levels.Count*32;Put(bytes,48,uvOffset);Put(bytes,52,uvOffset==0?0:levels.Count*16);
        int at=dataOffset;
        for(int i=levels.Count-1;i>=0;i--){
            var level=levels[i];int entry=64+i*32;Put(bytes,entry,level.Lod);BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(entry+4),(ushort)level.Width);BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(entry+6),(ushort)level.Height);Put(bytes,entry+8,format);Put(bytes,entry+16,at);Put(bytes,entry+20,level.Bytes.Length);Put(bytes,entry+24,(int)StreamableTextureDescriptor.ComputeCrc32(level.Bytes,0,level.Bytes.Length));
            if(uvOffset!=0){int u=uvOffset+i*16;Float(bytes,u,level.Uv.ScaleU);Float(bytes,u+4,level.Uv.ScaleV);Float(bytes,u+8,level.Uv.OffsetU);Float(bytes,u+12,level.Uv.OffsetV);}
            level.Bytes.CopyTo(bytes,at);at+=level.Bytes.Length;
        }
        Put(bytes,44,(int)StreamableTextureDescriptor.ComputeCrc32(bytes,64,metadata-64));
        var descriptor=StreamableTextureDescriptor.Read(bytes,total);
        string manifest=JsonSerializer.Serialize(new{format="DNLT",version=1,logicalWidth=high.Width,logicalHeight=high.Height,gxFormat=format,qualityPair=low!=null,alphaDecision=smooth?"rgba8-smooth-alpha":format==14?"cmpr-binary-alpha-coverage":"rgba8",gutter=options.Gutter,sourceRgbaSha256=Hash(high),lowRgbaSha256=low==null?null:Hash(low),levels=Enumerable.Range(0,descriptor.Count).Select(i=>{var l=descriptor.GetLevel(i);return new{logicalLod=l.LogicalLod,width=l.Width,height=l.Height,offset=l.Offset,bytes=l.Length,crc32=l.Crc32,scaleU=l.Uv.ScaleU,scaleV=l.Uv.ScaleV,offsetU=l.Uv.OffsetU,offsetV=l.Uv.OffsetV};})},new JsonSerializerOptions{WriteIndented=true})+"\n";
        return new StreamableTextureAsset(bytes,manifest);
    }
    private static Level Pack(RasterImage image,int lod,int format,int gutter)
    {
        int w=image.Width+2*gutter,h=image.Height+2*gutter;byte[] bytes=new byte[StreamableTextureDescriptor.NativeByteCount(w,h,format)];
        Rgba32 Pixel(int x,int y)=>image[Math.Clamp(x-gutter,0,image.Width-1),Math.Clamp(y-gutter,0,image.Height-1)];
        int at=0;
        if(format==6){
            for(int y=0;y<h;y+=4)for(int x=0;x<w;x+=4){for(int i=0;i<16;i++){var p=Pixel(x+i%4,y+i/4);bytes[at+i*2]=p.A;bytes[at+i*2+1]=p.R;bytes[at+32+i*2]=p.G;bytes[at+33+i*2]=p.B;}at+=64;}
        }else{
            var block=new Rgba32[16];for(int y=0;y<h;y+=8)for(int x=0;x<w;x+=8)for(int sub=0;sub<4;sub++){
                for(int i=0;i<16;i++)block[i]=Pixel(x+(sub%2)*4+i%4,y+(sub/2)*4+i/4);
                Compress(block,bytes,at);at+=8;
            }
        }
        return new Level(lod,w,h,bytes,new TextureLodUv(image.Width/(float)w,image.Height/(float)h,gutter/(float)w,gutter/(float)h));
    }
    private static ushort Color(Rgba32 p)=>(ushort)((p.R>>3)<<11|(p.G>>2)<<5|(p.B>>3));
    private static Rgba32 Color(int c)=>new((byte)(((c>>11)&31)*255/31),(byte)(((c>>5)&63)*255/63),(byte)((c&31)*255/31),255);
    private static int Distance(Rgba32 a,Rgba32 b)=>(a.R-b.R)*(a.R-b.R)+(a.G-b.G)*(a.G-b.G)+(a.B-b.B)*(a.B-b.B);
    private static void Compress(Rgba32[] pixels,byte[] output,int at)
    {
        bool transparent=false;int low=65535,high=0,covered=0;
        foreach(var p in pixels){if(p.A==0){transparent=true;continue;}int c=Color(p);low=Math.Min(low,c);high=Math.Max(high,c);covered++;}
        if(covered==0){low=high=0;transparent=true;}
        int c0=transparent?low:high,c1=transparent?high:low;
        if(!transparent&&c0==c1){if(c0<65535)c0++;else c1--;}
        var a=Color(c0);var b=Color(c1);var palette=new[]{a,b,new Rgba32((byte)((a.R*(transparent?1:2)+b.R)/(transparent?2:3)),(byte)((a.G*(transparent?1:2)+b.G)/(transparent?2:3)),(byte)((a.B*(transparent?1:2)+b.B)/(transparent?2:3)),255),new Rgba32((byte)((a.R+b.R*2)/3),(byte)((a.G+b.G*2)/3),(byte)((a.B+b.B*2)/3),255)};
        uint bits=0;foreach(var p in pixels){int choice=3;if(p.A!=0){choice=0;int distance=Distance(p,palette[0]);for(int i=1;i<(transparent?3:4);i++){int d=Distance(p,palette[i]);if(d<distance){choice=i;distance=d;}}}bits=(bits<<2)|(uint)choice;}
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(at),(ushort)c0);BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(at+2),(ushort)c1);BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(at+4),bits);
    }
}
