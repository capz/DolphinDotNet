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
