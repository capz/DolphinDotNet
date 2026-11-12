using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dolphin.Memory;
using Scheduler = Dolphin.Threading.Tasks.Scheduler;

namespace Dolphin.Graphics;

public enum AramTransferPriority { Demand=0, Prefetch=1, Background=2 }

/// <summary>Aligned DMA between ARAM and dedicated native MEM1 storage. The
/// shared broker arbitrates bounded chunks with terrain and model transfers;
/// conflicting ranges preserve submission order. Do not access a submitted
/// source/destination until its task completes.</summary>
public static class AramTransferQueue
{
    private static readonly List<Transfer> queue=new List<Transfer>(128);
    private static int next;
    // Retain the original signatures for compiled consumers.
    public static Task CopyToMainAsync(AramBuffer source,int sourceOffset,NativeBuffer destination,int destinationOffset,int count,CancellationToken cancellationToken=default)
        => Enqueue(source,sourceOffset,destination,destinationOffset,count,false,AramTransferPriority.Demand,cancellationToken);
    public static Task CopyToAramAsync(NativeBuffer source,int sourceOffset,AramBuffer destination,int destinationOffset,int count,CancellationToken cancellationToken=default)
        => Enqueue(destination,destinationOffset,source,sourceOffset,count,true,AramTransferPriority.Background,cancellationToken);
    public static Task CopyToMainAsync(AramBuffer source,int sourceOffset,NativeBuffer destination,int destinationOffset,int count,AramTransferPriority priority,CancellationToken cancellationToken=default)
        => Enqueue(source,sourceOffset,destination,destinationOffset,count,false,priority,cancellationToken);
    public static Task CopyToAramAsync(NativeBuffer source,int sourceOffset,AramBuffer destination,int destinationOffset,int count,AramTransferPriority priority,CancellationToken cancellationToken=default)
        => Enqueue(destination,destinationOffset,source,sourceOffset,count,true,priority,cancellationToken);
    private static Task Enqueue(AramBuffer aram,int aramOffset,NativeBuffer buffer,int bufferOffset,int count,bool write,AramTransferPriority priority,CancellationToken cancellation)
    {
        if(aram==null)throw new ArgumentNullException("aram");
        if(buffer==null)throw new ArgumentNullException("buffer");
        if((int)priority<0||(int)priority>2)throw new ArgumentOutOfRangeException("priority");
        if(aramOffset<0||bufferOffset<0||count<0||((aramOffset|bufferOffset|count)&31)!=0||
           aramOffset>aram.Length||count>aram.Length-aramOffset||
           bufferOffset>buffer.Length||count>buffer.Length-bufferOffset)
            throw new ArgumentOutOfRangeException("count","DMA ranges must be 32-byte aligned and in bounds.");
        int token=aram.NativeTransferToken;
        buffer.GetAddress(0);
        if(cancellation.IsCancellationRequested){var t=new TaskCompletionSource<int>();t.SetCanceled();return t.Task;}
        if(count==0)return Task.CompletedTask;
        if(queue.Count>=128)throw new InvalidOperationException("ARAM transfer queue full.");
        var work=new Transfer(buffer.Token,token,cancellation);
        if(NativeGraphics.AramTransferHold(token)<0)throw new InvalidOperationException("ARAM allocation is unavailable.");
        if(NativeGraphics.BufferHold(buffer.Token)<0){NativeGraphics.AramTransferRelease(token);throw new InvalidOperationException("Native buffer unavailable.");}
        int ticket=NativeGraphics.AramRequestNative(token,aramOffset,buffer.Token,bufferOffset,count,write?1:0,(int)priority);
        if(ticket<0){work.Release();throw new InvalidOperationException("ARAM broker refused the transfer or its queue is full.");}
        work.Request=ticket;
        queue.Add(work);
        Aram.WakeTransfers();
        if(queue.Count==1)
        {
            try{Scheduler.PostBackground(Pump);}
            catch
            {
                queue.RemoveAt(0);
                // Dispatch failure is an exceptional shutdown path. The broker
                // still owns its native addresses until cancellation drains.
                NativeGraphics.AramRequestCancel(ticket);
                while(NativeGraphics.AramRequestPoll(ticket)==0){}
                work.Release();throw;
            }
        }
        return work.Completion.Task;
    }
    private static void Pump()
    {
        try
        {
            if(next>=queue.Count)next=0;
            if(queue[next].Step())queue.RemoveAt(next);else next++;
            if(queue.Count!=0)Scheduler.PostBackground(Pump);else next=0;
        }
        catch(Exception error)
        {
            // A stopped/refusing dispatcher cannot own orphaned native DMA.
            // Only this exceptional path drains synchronously; normal work
            // yields after one broker poll and publishes on the application.
            for(int i=0;i<queue.Count;i++)
                try{queue[i].Abort(error);}catch{/* Owners drain before task notification. */}
            queue.Clear();next=0;
        }
    }
    private sealed class Transfer
    {
        private readonly int buffer,aram;
        private readonly CancellationToken cancellation;
        private bool canceled,released;
        private Exception? error;
        internal int Request;
        internal readonly TaskCompletionSource<int> Completion=new TaskCompletionSource<int>();
        internal Transfer(int buffer,int aram,CancellationToken cancellation)
        {this.buffer=buffer;this.aram=aram;this.cancellation=cancellation;}
        internal bool Step()
        {
            try
            {
                if(!canceled&&cancellation.IsCancellationRequested){NativeGraphics.AramRequestCancel(Request);canceled=true;}
                int result=NativeGraphics.AramRequestPoll(Request);
                if(result==0)return false;
                Request=0;
                cancellation.ThrowIfCancellationRequested();
                if(result<0)throw new global::System.IO.IOException("Native ARAM DMA failed.");
                Publish(null);
                return true;
            }
            catch(Exception failure)
            {
                if(Request!=0){Abort(failure);return true;}
                // Only terminal polls reach this path: cancellation cannot
                // release an owner still referenced by active DMA.
                Publish(failure);
                return true;
            }
        }
        private void Publish(Exception? failure)
        {
            error=failure;
            try{Scheduler.Post(Complete,0);}
            catch(Exception dispatchFailure)
            {
                if(error==null)error=dispatchFailure;
                Complete();
            }
        }
        private void Complete()
        {
            Release();
            if(Completion.Task.IsCompleted)return;
            if(error==null)Completion.SetResult(0);else Completion.SetException(error);
        }
        internal void Abort(Exception failure)
        {
            if(Request!=0)
            {
                NativeGraphics.AramRequestCancel(Request);
                while(NativeGraphics.AramRequestPoll(Request)==0){}
                Request=0;
            }
            error=failure;Complete();
        }
        internal void Release()
        {
            if(released)return;released=true;
            NativeGraphics.BufferUnhold(buffer);
            NativeGraphics.AramTransferRelease(aram);
        }
    }
}
