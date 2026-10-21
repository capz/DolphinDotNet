using System;
using System.Threading;
using System.Threading.Tasks;
namespace Dolphin.Graphics;

public sealed class Texture2D : IDisposable
{
    internal readonly int Token;
    private bool disposed;
    public int Width {get;}
    public int Height {get;}
    public int NativeBytes {get{Check();return NativeGraphics.DrawBytes(Token);}}
    private Texture2D(int token){Token=token;Width=NativeGraphics.TextureDimension(token,0);Height=NativeGraphics.TextureDimension(token,1);}
    public static Texture2D Create(int width,int height,byte[] rgba,bool linear=false)
    {
        if(rgba==null)throw new ArgumentNullException("rgba");
        int token=NativeGraphics.TextureCreate(width,height,rgba,linear?1:0);
        if(token==0)throw new ArgumentException("Invalid RGBA pixels, dimensions, memory budget or active frame.");
        return new Texture2D(token);
    }
    /// <summary>Decode a PNG in memory into a native tiled RGBA8 texture, preserving alpha.
    /// Call between frames. Encoded input is limited to 1 MiB, dimensions to 1024x1024,
    /// and decoder scratch allocations to 8 MiB. No file or asset converter is required.</summary>
    public static Texture2D FromPng(byte[] png,bool linear=false)
    {
        if(png==null)throw new ArgumentNullException("png");
        int token=NativeGraphics.TexturePng(png,linear?1:0);
        if(token==0)throw new ArgumentException("Invalid or unsupported PNG, dimensions, decoding budget or active frame.");
        return new Texture2D(token);
    }
    /// <summary>Create a GX-tiled, uncompressed-or-CMPR texture ready for repeated
    /// full-image replacement. Input bytes must use native GX tile order.</summary>
    public static Texture2D CreateNative(int width,int height,NativeTextureFormat format,bool linear=true)
    {
        if(format!=NativeTextureFormat.I4 && format!=NativeTextureFormat.I8 &&
           format!=NativeTextureFormat.Ia4 && format!=NativeTextureFormat.Ia8 &&
           format!=NativeTextureFormat.Rgb565 && format!=NativeTextureFormat.Rgb5A3 &&
           format!=NativeTextureFormat.Rgba8 && format!=NativeTextureFormat.Cmpr)
            throw new ArgumentOutOfRangeException("format");
        int token=NativeGraphics.TextureAllocateNative(width,height,(int)format,linear?1:0);
        if(token==0)throw new ArgumentException("Invalid native texture dimensions, format or memory budget.");
        return new Texture2D(token);
    }
    /// <summary>Replace the entire GX-native payload between frames. Synchronous;
    /// waits for prior GPU reads before writing and invalidates GX's texture cache.</summary>
    public void ReplaceNative(byte[] pixels,int sourceOffset=0)
    {
        Check();
        if(pixels==null)throw new ArgumentNullException("pixels");
        int bytes=NativeBytes;
        if(sourceOffset<0 || sourceOffset>pixels.Length || bytes>pixels.Length-sourceOffset)throw new ArgumentOutOfRangeException("sourceOffset");
        GraphicsDevice.Check(NativeGraphics.TextureUpdateNative(Token,pixels,sourceOffset,bytes),
            "Texture replacement requires a complete GX-native payload outside a frame.");
    }
    /// <summary>Replace the complete native texture payload from an aligned native
    /// staging buffer, without an intermediate managed array. Between frames only.</summary>
    public void ReplaceNative(NativeBuffer source,int sourceOffset=0)
    {
        Check();
        if(source==null)throw new ArgumentNullException("source");
        source.GetAddress(0);
        int bytes=NativeBytes;
        if(sourceOffset<0 || sourceOffset>source.Length || bytes>source.Length-sourceOffset)
            throw new ArgumentOutOfRangeException("sourceOffset");
        GraphicsDevice.Check(NativeGraphics.TextureUpdateFromBuffer(Token,source.Token,sourceOffset,bytes),
            "Native texture replacement requires a valid source and no active frame.");
    }
    public static Texture2D FromPackage(byte[] package,bool linear=false)
    {
        if(package==null)throw new ArgumentNullException("package");int token=NativeGraphics.TexturePackage(package,linear?1:0);
        if(token==0)throw new ArgumentException("Invalid DNTX texture, budget or active frame.");return new Texture2D(token);
    }
    public static async Task<Texture2D> LoadAsync(string path,CancellationToken cancellationToken=default,bool linear=false)
    {
        var bytes=await Dolphin.IO.File.ReadAllBytesAsync(path,cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();return FromPackage(bytes,linear);
    }
    internal void Check(){if(disposed)throw new ObjectDisposedException("Texture2D");}
    public void Dispose(){if(disposed)return;GraphicsDevice.Check(NativeGraphics.DrawRelease(Token),"Cannot dispose texture during a raw stream.");disposed=true;}
}
