using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Dolphin.Graphics;
using Scheduler = Dolphin.Threading.Tasks.Scheduler;

namespace Dolphin.Storage;

/// <summary>Bounded random-access reads into native MEM1 buffers. The file read
/// executes on the background executor, and publication happens on the application
/// executor between graphics frames. One position-changing read is active per stream.
/// Callers must not concurrently use the target buffer for rendering/DMA.</summary>
public sealed class AssetStream : IDisposable
{
    private readonly FileStream stream;
    // One read is active per stream; reserve its reusable workspace once.
    private readonly byte[] staging=new byte[8192];
    private bool disposed;
    private bool busy;
    public long Length {get;}
    public AssetStream(string path)
    {
        if(path==null)throw new ArgumentNullException("path");
        stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        Length=stream.Length;
    }
    public Task<int> ReadAtAsync(long offset,NativeBuffer destination,int destinationOffset,int count,CancellationToken cancellationToken=default)
    {
        if(disposed)throw new ObjectDisposedException("AssetStream");
        if(destination==null)throw new ArgumentNullException("destination");
        if(offset<0 || offset>Length || count<0 || count>Length-offset ||
           destinationOffset<0 || destinationOffset>destination.Length || count>destination.Length-destinationOffset)
            throw new ArgumentOutOfRangeException("count");
        destination.GetAddress(0);
        if(busy)throw new InvalidOperationException("An asset read is already outstanding.");
        if(cancellationToken.IsCancellationRequested){var canceled=new TaskCompletionSource<int>();canceled.SetCanceled();return canceled.Task;}
        if(count==0)return Task.FromResult(0);
        busy=true;
        var work=new ReadWork(this,offset,destination,destinationOffset,count,cancellationToken);
        try{Scheduler.PostBackground(work.Step);}
        catch{busy=false;throw;}
        return work.Completion.Task;
    }
    public void Dispose()
    {
        if(disposed)return;
        disposed=true;
        if(!busy)stream.Dispose();
    }
    private sealed class ReadWork
    {
        private readonly AssetStream owner;
        private readonly NativeBuffer destination;
        private readonly CancellationToken cancellation;

        private readonly int destinationOffset,count;
        private long offset;
        private int done,pending;
        internal readonly TaskCompletionSource<int> Completion=new TaskCompletionSource<int>();
        internal ReadWork(AssetStream owner,long offset,NativeBuffer destination,int destinationOffset,int count,CancellationToken cancellation)
        {this.owner=owner;this.offset=offset;this.destination=destination;this.destinationOffset=destinationOffset;this.count=count;this.cancellation=cancellation;}
        internal void Step()
        {
            try
            {
                cancellation.ThrowIfCancellationRequested();
                if(owner.disposed)throw new ObjectDisposedException("AssetStream");
                owner.stream.Seek(offset+done,SeekOrigin.Begin);
                int needed=count-done;if(needed>owner.staging.Length)needed=owner.staging.Length;
                pending=owner.stream.Read(owner.staging,0,needed);
                Scheduler.Post(Publish,0);
            }
            catch(Exception ex){Fail(ex);}
        }
        private void Publish()
        {
            try
            {
                cancellation.ThrowIfCancellationRequested();
                if(owner.disposed)throw new ObjectDisposedException("AssetStream");
                if(pending>0){destination.Write(owner.staging,0,destinationOffset+done,pending);done+=pending;}
                if(done==count || pending==0)
                {
                    int result=done;
                    Complete();Completion.SetResult(result);
                }
                else Scheduler.PostBackground(Step);
            }
            catch(Exception ex){Fail(ex);}
        }
        private void Complete()
        {
            owner.busy=false;
            if(owner.disposed)owner.stream.Dispose();
        }
        private void Fail(Exception ex){Complete();Completion.SetException(ex);}
    }
}
