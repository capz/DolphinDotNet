using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Scheduler = Dolphin.Threading.Tasks.Scheduler;
namespace Dolphin.Memory;

public sealed class AramStatistics
{
    public int CapacityBytes {get;}
    public int AllocatedBytes {get;}
    public int FreeBytes {get;}
    public int LargestFreeBlockBytes {get;}
    public int AllocationCount {get;}
    internal AramStatistics(){CapacityBytes=NativeAram.AramStat(0);AllocatedBytes=NativeAram.AramStat(1);FreeBytes=NativeAram.AramStat(2);LargestFreeBlockBytes=NativeAram.AramStat(3);AllocationCount=NativeAram.AramStat(4);}
}
public static class Aram
{
    /// <summary>Initialize before any other ARAM owner. Reserve lower user memory for audio/platform services.</summary>
    public static bool Initialize(int reservedBytes=1024*1024)
    {
        if(reservedBytes<0 || reservedBytes%32!=0)throw new ArgumentOutOfRangeException("reservedBytes");
        return NativeAram.AramInitialize(reservedBytes);
    }
    public static bool IsAvailable=>NativeAram.AramAvailable();
    public static AramStatistics Statistics=>new AramStatistics();
    public static bool TryAllocate(int length,out AramBuffer buffer)
    {
        if(length<=0)throw new ArgumentOutOfRangeException("length");
        if(!IsAvailable)throw new InvalidOperationException("Initialize ARAM first.");
        var token=NativeAram.AramAllocate(length);
        if(token==0){buffer=null!;return false;}
        try{buffer=new AramBuffer(token,length);return true;}catch{NativeAram.AramDispose(token);throw;}
    }
    public static AramBuffer Allocate(int length)
    {
        if(!TryAllocate(length,out var buffer))throw new OutOfMemoryException("ARAM is exhausted or fragmented.");
        return buffer;
    }
}
public sealed class AramBuffer : IDisposable
{
    private readonly int token;
    private bool disposed;
    public int Length {get;}
    internal AramBuffer(int token,int length){this.token=token;Length=length;}
    public Task WriteAsync(byte[] source,int sourceOffset,int destinationOffset,int count,CancellationToken cancellationToken=default)=>Transfer(source,sourceOffset,destinationOffset,count,true,cancellationToken);
    public Task ReadAsync(int sourceOffset,byte[] destination,int destinationOffset,int count,CancellationToken cancellationToken=default)=>Transfer(destination,destinationOffset,sourceOffset,count,false,cancellationToken);
    private Task Transfer(byte[] array,int arrayOffset,int offset,int count,bool write,CancellationToken cancellation)
    {
        if(disposed)throw new ObjectDisposedException("AramBuffer");
        if(array==null)throw new ArgumentNullException("array");
        if(arrayOffset<0 || offset<0 || count<0 || arrayOffset>array.Length || count>array.Length-arrayOffset || offset>Length || count>Length-offset)throw new ArgumentOutOfRangeException("count");
        if(cancellation.IsCancellationRequested)return Task.FromCanceled(cancellation);
        if(count==0)return Task.CompletedTask;
        var work=new TransferWork(token,array,arrayOffset,offset,count,write,cancellation);
        if(NativeAram.AramRetain(token)<0)throw new InvalidOperationException("Invalid ARAM allocation.");
        try{TransferQueue.Enqueue(work);}catch{NativeAram.AramRelease(token);throw;}
        return work.Completion.Task;
    }
    public void Dispose(){if(!disposed){disposed=true;NativeAram.AramDispose(token);}}
}
internal sealed class TransferWork
{
    private readonly int token;private readonly byte[] array;private readonly int arrayOffset,offset,count;private readonly bool write;private readonly CancellationToken cancellation;
    private int position,chunk;private bool pending;
    internal readonly TaskCompletionSource<int> Completion=new TaskCompletionSource<int>();
    internal TransferWork(int token,byte[] array,int arrayOffset,int offset,int count,bool write,CancellationToken cancellation){this.token=token;this.array=array;this.arrayOffset=arrayOffset;this.offset=offset;this.count=count;this.write=write;this.cancellation=cancellation;}
    internal bool Step()
    {
        try
        {
            // Drain active DMA before cancellation releases its allocation/staging buffer.
            if(pending)
            {
                var result=NativeAram.AramPoll(array,arrayOffset+position);
                if(result<0)throw new System.IO.IOException("ARAM transfer failed.");
                if(result==0)return false;
                pending=false;position+=chunk;
            }
            cancellation.ThrowIfCancellationRequested();
            if(position==count){NativeAram.AramRelease(token);Completion.SetResult(position);return true;}
            chunk=count-position;if(chunk>4096)chunk=4096;
            if(NativeAram.AramBegin(token,offset+position,array,arrayOffset+position,chunk,write?1:0)<0)throw new System.IO.IOException("ARAM transfer could not start.");
            pending=true;return false;
        }
        catch(Exception error){NativeAram.AramRelease(token);Completion.SetException(error);return true;}
    }
}
internal static class TransferQueue
{
    private static readonly List<TransferWork> queue=new List<TransferWork>();
    internal static void Enqueue(TransferWork work)
    {
        if(queue.Count>=128)throw new InvalidOperationException("ARAM transfer queue is full.");
        queue.Add(work);
        if(queue.Count==1){try{Scheduler.Post(Pump);}catch{queue.RemoveAt(0);throw;}}
    }
    private static void Pump(){if(queue[0].Step())queue.RemoveAt(0);if(queue.Count!=0)Scheduler.Post(Pump);}
}
