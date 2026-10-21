using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dolphin.Memory;
using Scheduler = Dolphin.Threading.Tasks.Scheduler;

namespace Dolphin.Graphics;

/// <summary>Aligned direct DMA between ARAM and native MEM1 storage. Dedicated staging
/// buffers must not be accessed by the CPU/GPU until completion. Serializes with
/// ordinary AramBuffer transfers via the shared ARQ backend.</summary>
public static class AramTransferQueue
{
    private const int ChunkSize=65536;
    private static readonly List<Transfer> queue=new List<Transfer>();
    public static Task CopyToMainAsync(AramBuffer source,int sourceOffset,NativeBuffer destination,int destinationOffset,int count,CancellationToken cancellationToken=default)
        => Enqueue(source,sourceOffset,destination,destinationOffset,count,false,cancellationToken);
    public static Task CopyToAramAsync(NativeBuffer source,int sourceOffset,AramBuffer destination,int destinationOffset,int count,CancellationToken cancellationToken=default)
        => Enqueue(destination,destinationOffset,source,sourceOffset,count,true,cancellationToken);
    private static Task Enqueue(AramBuffer aram,int aramOffset,NativeBuffer buffer,int bufferOffset,int count,bool write,CancellationToken cancellation)
    {
        if(aram==null)throw new ArgumentNullException("aram");
        if(buffer==null)throw new ArgumentNullException("buffer");
        if(aramOffset<0 || bufferOffset<0 || count<0 || ((aramOffset|bufferOffset|count)&31)!=0 ||
           aramOffset>aram.Length || count>aram.Length-aramOffset ||
           bufferOffset>buffer.Length || count>buffer.Length-bufferOffset)
            throw new ArgumentOutOfRangeException("count","DMA ranges must be 32-byte aligned and in bounds.");
        int token=aram.NativeTransferToken;
        buffer.GetAddress(0); // Verify the managed owner has not been disposed.
        if(cancellation.IsCancellationRequested){var t=new TaskCompletionSource<int>();t.SetCanceled();return t.Task;}
        if(count==0)return Task.CompletedTask;
        if(NativeGraphics.AramTransferHold(token)<0)throw new InvalidOperationException("ARAM allocation is unavailable.");
        if(NativeGraphics.BufferHold(buffer.Token)<0){NativeGraphics.AramTransferRelease(token);throw new InvalidOperationException("Native buffer unavailable.");}
        var work=new Transfer(token,aramOffset,buffer.Token,bufferOffset,count,write,cancellation);
        if(queue.Count>=128)
        {
            NativeGraphics.BufferUnhold(buffer.Token);NativeGraphics.AramTransferRelease(token);
            throw new InvalidOperationException("ARAM transfer queue full.");
        }
        queue.Add(work);
        if(queue.Count==1)
        {
            try{Scheduler.PostBackground(Pump);}
            catch
            {
                queue.RemoveAt(0);NativeGraphics.BufferUnhold(buffer.Token);NativeGraphics.AramTransferRelease(token);
                throw;
            }
        }
        return work.Completion.Task;
    }
    private static void Pump()
    {
        if(queue[0].Step())queue.RemoveAt(0);
        if(queue.Count!=0)Scheduler.PostBackground(Pump);
    }
    private sealed class Transfer
    {
        private readonly int aram,buffer,aramOffset,bufferOffset,count;
        private readonly bool write;
        private readonly CancellationToken cancellation;
        private int position,chunk;
        private bool pending;
        internal readonly TaskCompletionSource<int> Completion=new TaskCompletionSource<int>();
        internal Transfer(int aram,int aramOffset,int buffer,int bufferOffset,int count,bool write,CancellationToken cancellation)
        {this.aram=aram;this.aramOffset=aramOffset;this.buffer=buffer;this.bufferOffset=bufferOffset;this.count=count;this.write=write;this.cancellation=cancellation;}
        internal bool Step()
        {
            try
            {
                if(pending)
                {
                    int result=NativeGraphics.AramNativePoll();
                    if(result<0)throw new System.IO.IOException("Native ARAM DMA failed.");
                    if(result==0)return false;
                    pending=false;position+=chunk;
                }
                cancellation.ThrowIfCancellationRequested();
                if(position==count){Finish();Completion.SetResult(position);return true;}
                chunk=count-position;if(chunk>ChunkSize)chunk=ChunkSize;
                if(NativeGraphics.AramNativeBegin(aram,aramOffset+position,buffer,bufferOffset+position,chunk,write?1:0)<0)
                    throw new System.IO.IOException("Native ARAM DMA could not start.");
                pending=true;return false;
            }
            catch(Exception ex)
            {
                // Failed transfers are expected to have completed or been refused.
                Finish();Completion.SetException(ex);return true;
            }
        }
        private void Finish()
        {
            NativeGraphics.BufferUnhold(buffer);
            NativeGraphics.AramTransferRelease(aram);
        }
    }
}
