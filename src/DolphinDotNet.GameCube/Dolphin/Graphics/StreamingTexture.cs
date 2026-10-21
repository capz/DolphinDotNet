using System;
namespace Dolphin.Graphics;

/// <summary>Owns one reusable GX-native texture allocation with explicit resident
/// generation. Publication is synchronous between frames; background storage
/// and ARAM transfers should finish before calling UploadNative.</summary>
public sealed class StreamingTexture : IDisposable
{
    private readonly Texture2D texture;
    private bool disposed,resident;
    private int generation;
    public Texture2D Texture {get{Check();return texture;}}
    public bool IsResident {get{Check();return resident;}}
    public int ContentGeneration {get{Check();return generation;}}
    public int NativeBytes {get{Check();return texture.NativeBytes;}}
    public StreamingTexture(int width,int height,NativeTextureFormat format=NativeTextureFormat.Cmpr,bool linear=true)
    {
        texture=Texture2D.CreateNative(width,height,format,linear);
    }
    public void UploadNative(byte[] nativePixels,int offset=0)
    {
        Check();
        texture.ReplaceNative(nativePixels,offset);
        resident=true;
        if(generation<int.MaxValue)generation++;
    }
    /// <summary>Publish a full page from MEM1 staging without managed copying.</summary>
    public void UploadNative(NativeBuffer source,int sourceOffset=0)
    {
        Check();
        texture.ReplaceNative(source,sourceOffset);
        resident=true;
        if(generation<int.MaxValue)generation++;
    }
    private void Check(){if(disposed)throw new ObjectDisposedException("StreamingTexture");}
    public void Dispose()
    {
        if(disposed)return;
        texture.Dispose();disposed=true;resident=false;
    }
}
