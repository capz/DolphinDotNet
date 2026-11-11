using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Dolphin.Graphics;

/// <summary>One private single-level allocation. Queue sequential aligned native
/// chunks between frames; the service executor copies/flushes CPU memory only.
/// A failed/cancelled upload cannot publish or invalidate the visible fallback.</summary>
public sealed class DetachedTextureUpload : IDisposable
{
    private Texture2D? texture;
    private readonly CancellationTokenSource cancellation=new CancellationTokenSource();
    private readonly uint expectedCrc;
    private Task? pending;
    private int completed,pendingBytes;
    private uint crc,pendingCrc;
    private bool failed,sealedUpload,aborting,disposed;
    public int ExpectedBytes {get;}
    public int CompletedBytes {get{Check();Observe();return completed;}}
    public int StagedBytes=>texture==null?0:ExpectedBytes;
    public bool HasPendingCopy=>pending!=null&&!pending.IsCompleted;
    private DetachedTextureUpload(Texture2D target,int bytes,uint checksum){texture=target;ExpectedBytes=bytes;expectedCrc=checksum;}
    private static void Application(){if(!Dolphin.Threading.Tasks.Scheduler.IsApplicationThread)throw new InvalidOperationException("Use the application executor for upload ownership and publication.");}
    private void Check(){Application();if(disposed)throw new ObjectDisposedException("DetachedTextureUpload");}
    public static DetachedTextureUpload Create(int width,int height,NativeTextureFormat format,int expectedNativeBytes,uint expectedCrc32)
    {
        Application();var target=Texture2D.CreateDetached(width,height,format);
        if(target.NativeBytes!=expectedNativeBytes){target.Dispose();throw new ArgumentException("Native payload size differs from texture allocation.");}
        return new DetachedTextureUpload(target,expectedNativeBytes,expectedCrc32);
    }
    private void Observe()
    {
        if(pending==null)return;if(!pending.IsCompleted)throw new InvalidOperationException("Wait for the pending chunk before reusing its range.");
        var work=pending;pending=null;
        try{work.GetAwaiter().GetResult();completed+=pendingBytes;crc=pendingCrc;}
        catch{failed=true;throw;}
    }
    /// <summary>Source ownership is retained through cancellation and completion.
    /// A held source cannot be overwritten through NativeBuffer.Write. Chunks
    /// must be contiguous, nonoverlapping, 32-byte aligned and complete.</summary>
    public Task StageChunkAsync(NativeBuffer source,int sourceOffset,int destinationOffset,int count,CancellationToken cancellationToken=default)
    {
        Check();Observe();if(texture==null||failed||aborting||sealedUpload)throw new InvalidOperationException("Upload no longer accepts chunks.");
        if(source==null)throw new ArgumentNullException("source");source.GetAddress(0);
        if(sourceOffset<0||destinationOffset!=completed||count<=0||((sourceOffset|destinationOffset|count)&31)!=0||sourceOffset>source.Length||count>source.Length-sourceOffset||count>ExpectedBytes-completed)throw new ArgumentOutOfRangeException("count");
        cancellationToken.ThrowIfCancellationRequested();cancellation.Token.ThrowIfCancellationRequested();
        pendingCrc=source.ComputeCrc32(sourceOffset,count,crc);pendingBytes=count;
        if(NativeGraphics.BufferHold(source.Token)<0)throw new InvalidOperationException("Cannot hold source storage.");
        if(NativeGraphics.TextureUploadHold(texture.Token)<0){NativeGraphics.BufferUnhold(source.Token);throw new InvalidOperationException("Cannot hold detached destination.");}
        try{pending=Copy(texture.Token,source.Token,sourceOffset,destinationOffset,count,cancellation.Token,cancellationToken);return pending;}
        catch{NativeGraphics.TextureUploadUnhold(texture.Token);NativeGraphics.BufferUnhold(source.Token);throw;}
    }
    private static async Task Copy(int target,int source,int sourceOffset,int destinationOffset,int count,CancellationToken stop,CancellationToken token)
    {
        try{
            await Task.Run(()=>{
                token.ThrowIfCancellationRequested();stop.ThrowIfCancellationRequested();
                if(NativeGraphics.TextureStageBufferRange(target,source,sourceOffset,destinationOffset,count)<0)throw new InvalidOperationException("Detached range copy failed.");
            });
        }finally{NativeGraphics.TextureUploadUnhold(target);NativeGraphics.BufferUnhold(source);}
    }
    public void Seal()
    {
        Check();Observe();if(failed||aborting||texture==null)throw new InvalidOperationException("Upload cannot be sealed.");
        if(completed!=ExpectedBytes)throw new InvalidOperationException("Upload has missing ranges.");
        if(crc!=expectedCrc){failed=true;throw new InvalidDataException("Detached texture payload CRC mismatch.");}
        sealedUpload=true;
    }
    public bool TryPublish(out Texture2D? published)
    {
        Check();published=null;if(aborting||failed||!sealedUpload||texture==null||HasPendingCopy)return false;Observe();
        texture.PublishDetached();published=texture;texture=null;return true;
    }
    /// <summary>Cancel queued work, join any started CPU copy, then release the
    /// never-visible destination. Source and destination remain held until join.</summary>
    public async Task AbortAsync()
    {
        Check();aborting=true;cancellation.Cancel();var work=pending;
        if(work!=null)try{await work;}catch(Exception){}pending=null;
        texture?.Dispose();texture=null;disposed=true;cancellation.Dispose();
    }
    public void Dispose()
    {
        if(disposed)return;Application();aborting=true;cancellation.Cancel();
        if(HasPendingCopy)throw new InvalidOperationException("Await AbortAsync before disposing a running copy.");
        if(pending!=null){try{Observe();}catch(Exception){}}texture?.Dispose();texture=null;disposed=true;cancellation.Dispose();
    }
}
