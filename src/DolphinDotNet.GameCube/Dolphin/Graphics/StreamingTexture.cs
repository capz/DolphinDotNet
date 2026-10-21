using System;
using System.Threading;
using System.Threading.Tasks;

namespace Dolphin.Graphics;

/// <summary>Double-buffered GX-native streaming texture. CPU staging is asynchronous
/// and may run during a frame; publishing is an explicit application-thread swap.
/// The caller must rebind Material.Texture to the new Texture after TryPublish.
/// Neither staging nor publishing waits for GX completion.</summary>
public sealed class StreamingTexture : IDisposable
{
    private readonly Texture2D[] buffers;
    private int front=0,generation;
    private bool disposed,resident;
    private Task? staging;
    private GpuFence previousFrame;

    public Texture2D Texture {get{Check();return buffers[front];}}
    public bool IsResident {get{Check();return resident;}}
    public int ContentGeneration {get{Check();return generation;}}
    public int NativeBytes {get{Check();return buffers[front].NativeBytes;}}
    public bool HasPendingUpload {get{Check();return staging!=null;}}
    public StreamingTexture(int width,int height,NativeTextureFormat format=NativeTextureFormat.Cmpr,bool linear=true)
    {
        buffers=new Texture2D[2];
        buffers[0]=Texture2D.CreateNative(width,height,format,linear);
        try{buffers[1]=Texture2D.CreateNative(width,height,format,linear);}
        catch {buffers[0].Dispose();throw;}
    }
    private void Check(){if(disposed)throw new ObjectDisposedException("StreamingTexture");}
    private void ValidateUpload(int offset,int size)
    {
        Check();
        if(staging!=null)throw new InvalidOperationException("Publish or handle the previous staged upload first.");
        if(previousFrame.IsValid && !previousFrame.IsCompleted)
            throw new InvalidOperationException("Inactive texture is still referenced by the previous GPU frame.");
        if(offset<0 || offset>size || NativeBytes>size-offset)throw new ArgumentOutOfRangeException("offset");
    }
    /// <summary>Queue a CPU-only GX-format page copy to the inactive allocation.
    /// Keep source bytes immutable until the returned Task completes.</summary>
    public Task StageUploadAsync(byte[] nativePixels,int offset=0,CancellationToken cancellationToken=default)
    {
        if(nativePixels==null)throw new ArgumentNullException("nativePixels");
        ValidateUpload(offset,nativePixels.Length);
        // Resolve the managed texture and its native size on the application
        // executor. The worker performs only the bounded native memory copy.
        int destinationToken=buffers[1-front].Token;
        int bytes=buffers[1-front].NativeBytes;
        staging=Task.Run(()=>{
            cancellationToken.ThrowIfCancellationRequested();
            if(NativeGraphics.TextureStageArray(destinationToken,nativePixels,offset,bytes)<0)
                throw new InvalidOperationException("Background GX-native page staging failed.");
        },cancellationToken);
        return staging;
    }
    /// <summary>Queue a native MEM1 page copy; its source allocation is pinned
    /// until the asynchronous transfer completes, including on cancellation.</summary>
    public Task StageUploadAsync(NativeBuffer source,int offset=0,CancellationToken cancellationToken=default)
    {
        if(source==null)throw new ArgumentNullException("source");
        source.GetAddress(0);
        ValidateUpload(offset,source.Length);
        if(NativeGraphics.BufferHold(source.Token)<0)throw new InvalidOperationException("Cannot retain native staging buffer.");
        try
        {
            var task=StageBufferAsync(buffers[1-front].Token,source.Token,
                buffers[1-front].NativeBytes,offset,cancellationToken);
            staging=task;return task;
        }
        catch{NativeGraphics.BufferUnhold(source.Token);throw;}
    }
    private static async Task StageBufferAsync(int targetToken,int sourceToken,int bytes,int offset,CancellationToken cancellationToken)
    {
        try
        {
            await Task.Run(()=>{
                cancellationToken.ThrowIfCancellationRequested();
                if(NativeGraphics.TextureStageBuffer(targetToken,sourceToken,offset,bytes)<0)
                    throw new InvalidOperationException("Background native-buffer page staging failed.");
            },cancellationToken);
        }
        finally{NativeGraphics.BufferUnhold(sourceToken);}
    }
    /// <summary>Nonblocking publication, valid between GX primitives even during
    /// an active graphics frame. Returns false while the background copy is pending.</summary>
    public bool TryPublish()
    {
        Check();
        if(staging==null || !staging.IsCompleted)return false;
        try{staging.GetAwaiter().GetResult();}
        catch{staging=null;throw;} // Failed/cancelled page never becomes visible.
        var back=buffers[1-front];
        back.PublishDetached(); // GX cache invalidate; no GX_DrawDone.
        previousFrame=GraphicsDevice.Default.IsFrameActive?GraphicsDevice.Default.InsertFence():default;
        front=1-front;resident=true;
        if(generation<int.MaxValue)generation++;
        staging=null;
        return true;
    }
    /// <summary>Publish and immediately update the supplied material's primary
    /// texture binding. Other materials using this page require their own rebinding.</summary>
    public bool TryPublish(Material material)
    {
        if(material==null)throw new ArgumentNullException("material");
        if(!TryPublish())return false;
        material.Texture=Texture;
        return true;
    }
    /// <summary>Legacy synchronous upload to the currently exposed texture,
    /// between frames only. Prefer StageUploadAsync + TryPublish.</summary>
    public void UploadNative(byte[] nativePixels,int offset=0)
    {
        Check();if(staging!=null)throw new InvalidOperationException("Upload pending.");
        buffers[front].ReplaceNative(nativePixels,offset);
        resident=true;if(generation<int.MaxValue)generation++;
    }
    public void UploadNative(NativeBuffer source,int sourceOffset=0)
    {
        Check();if(staging!=null)throw new InvalidOperationException("Upload pending.");
        buffers[front].ReplaceNative(source,sourceOffset);
        resident=true;if(generation<int.MaxValue)generation++;
    }
    public void Dispose()
    {
        if(disposed)return;
        if(staging!=null && !staging.IsCompleted)throw new InvalidOperationException("Cannot dispose while a page copy is running.");
        // If invoked mid-frame the managed resource owners already defer GPU frees.
        buffers[0].Dispose();buffers[1].Dispose();disposed=true;resident=false;
    }
}
