using System;
using System.Collections.Generic;
namespace Dolphin.Graphics;

/// <summary>The application's shared graphics owner. Submission is synchronous on the application executor.</summary>
public sealed class GraphicsDevice
{
    public static GraphicsDevice Default {get;}=new GraphicsDevice();
    private readonly List<RetiredResource> retired=new List<RetiredResource>();
    private sealed class RetiredResource
    {
        internal readonly GpuFence Fence;
        internal readonly IDisposable Resource;
        internal RetiredResource(GpuFence fence,IDisposable resource){Fence=fence;Resource=resource;}
    }
    private GraphicsDevice() { }
    public int Width => NativeGraphics.DeviceWidth();
    public int Height => NativeGraphics.DeviceHeight();
    public bool IsFrameActive => NativeGraphics.DeviceActive()!=0;
    public int NativeBytes => NativeGraphics.DrawTotalBytes();
    public void BeginFrame(Color32 clear)
        => Check(NativeGraphics.DeviceBegin(clear.R/255f,clear.G/255f,clear.B/255f,clear.A/255f),"A graphics frame is already active or raw code owns initialization.");
    public void EndFrame()
    {
        Check(NativeGraphics.DeviceEnd(),"End every drawing pass before presenting the frame.");
        CollectRetired();
    }
    /// <summary>Conservative fence for the current frame. Completion is signaled
    /// only after the present path has waited for GPU completion.</summary>
    public GpuFence InsertFence()
    {
        int value=NativeGraphics.DeviceFenceInsert();
        if(value==0)throw new InvalidOperationException("InsertFence requires an active frame without a raw primitive.");
        return new GpuFence(value);
    }
    /// <summary>Retain ownership until a submitted frame has completed.
    /// Do not submit the resource again after retirement.</summary>
    public void RetireAfter(GpuFence fence,IDisposable resource)
    {
        if(resource==null)throw new ArgumentNullException("resource");
        if(!fence.IsValid)throw new ArgumentException("Invalid fence.");
        if(retired.Count>=512)throw new InvalidOperationException("GPU retirement queue full.");
        if(fence.IsCompleted){resource.Dispose();return;}
        retired.Add(new RetiredResource(fence,resource));
    }
    public void CollectRetired()
    {
        for(int i=retired.Count-1;i>=0;i--)
            if(retired[i].Fence.IsCompleted)
            {
                var resource=retired[i].Resource;
                retired.RemoveAt(i);
                resource.Dispose();
            }
    }
    public void BeginRaw() => Check(NativeGraphics.DeviceRawBegin(),"A raw pass requires an active frame with no other pass.");
    public void EndRaw() => Check(NativeGraphics.DeviceRawEnd(),"Close the raw primitive/display list before ending the pass.");
    public void SetViewport(int x,int y,int width,int height) => Check(NativeGraphics.DeviceViewport(x,y,width,height),"Invalid viewport or active drawing pass.");
    public void SetScissor(int x,int y,int width,int height) => Check(NativeGraphics.DeviceScissor(x,y,width,height),"Invalid scissor or active drawing pass.");
    public void WaitIdle() => Check(NativeGraphics.DeviceWait(),"Cannot wait inside a raw primitive/display list.");
    public void CopyToTexture(Texture2D destination,int x,int y)
    {
        if(destination==null)throw new ArgumentNullException("destination");destination.Check();
        Check(NativeGraphics.DrawCopyTexture(destination.Token,x,y,destination.Width,destination.Height),"Copy requires an active frame between passes and an RGBA8 destination that fits the EFB.");
    }
    internal static void Check(int result,string message){if(result<0)throw new InvalidOperationException(message);}
}
