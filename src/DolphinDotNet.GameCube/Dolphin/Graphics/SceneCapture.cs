using System;
namespace Dolphin.Graphics;

/// <summary>Owns an RGBA8 EFB capture texture. Capturing requires an active device
/// frame between passes, synchronizes GX, and copies the shared EFB without clearing it.
/// This is an explicit copy, not an independent framebuffer or render target.</summary>
public sealed class SceneCapture : IDisposable
{
    private readonly GraphicsDevice device;
    private bool disposed;
    public Texture2D Texture {get;}
    public int NativeBytes {get{Check();return Texture.NativeBytes;}}
    public SceneCapture(GraphicsDevice device,int width,int height,bool linear=true)
    {
        if(device==null)throw new ArgumentNullException("device");
        if(width<=0||height<=0||width%4!=0||height%4!=0)throw new ArgumentException("Capture dimensions must be positive multiples of four.");
        this.device=device;Texture=Texture2D.CreateNative(width,height,NativeTextureFormat.Rgba8,linear);
    }
    private void Check(){if(disposed)throw new ObjectDisposedException("SceneCapture");Texture.Check();}
    /// <summary>Source must equal the destination size or be exactly twice as
    /// large in both dimensions (GX half-size copy). Source X/Y must be even.</summary>
    public void Capture(Viewport source)
    {Check();device.CaptureToTexture(Texture,source);}
    public void Draw(Draw2D batch,RectF destination,Color32 tint)
    {Check();if(batch==null)throw new ArgumentNullException("batch");batch.DrawTexture(Texture,destination,tint);}
    public void Dispose(){if(disposed)return;Texture.Dispose();disposed=true;}
}
public sealed partial class GraphicsDevice
{
    /// <summary>Explicit synchronized EFB copy. Requires a non-mipmapped RGBA8
    /// target with dimensions divisible by four. Source origin must be even;
    /// source is target-size or twice target-size. Does not clear or isolate EFB.</summary>
    public void CaptureToTexture(Texture2D destination,Viewport source)
    {
        if(destination==null)throw new ArgumentNullException("destination");destination.Check();
        if(destination.Width%4!=0||destination.Height%4!=0||source.X%2!=0||source.Y%2!=0)
            throw new ArgumentException("Capture dimensions must be multiples of four and source origin even.");
        Check(NativeGraphics.DrawCopyTexture(destination.Token,source.X,source.Y,source.Width,source.Height),
            "Capture requires an active frame between passes and an aligned RGBA8 copy region.");
    }
}
