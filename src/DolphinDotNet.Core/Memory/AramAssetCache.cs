using System;
using System.Collections.Generic;
using Dolphin.Threading;
using Dolphin.Threading.Tasks;
namespace Dolphin.Memory;

/// <summary>Bounded cold-asset cache. Lower-priority, least-recently-used recoverable entries are evicted first.</summary>
public sealed class AramAssetCache : IDisposable
{
    private sealed class Entry
    {
        internal readonly string Id;internal readonly AramBuffer Buffer;internal readonly int Bytes,Priority;internal readonly bool Evictable;
        internal Entry(string id,AramBuffer buffer,int bytes,int priority,bool evictable){Id=id;Buffer=buffer;Bytes=bytes;Priority=priority;Evictable=evictable;}
    }
    private readonly List<Entry> entries=new List<Entry>();
    private readonly int budget;private int used;private bool disposed,storing;
    public int CapacityBytes=>budget;
    public int UsedBytes=>used;
    public AramAssetCache(int capacityBytes)
    {
        if(!Aram.IsAvailable)throw new InvalidOperationException("Initialize ARAM first.");
        if(capacityBytes<=0 || capacityBytes>Aram.Statistics.CapacityBytes)throw new ArgumentOutOfRangeException("capacityBytes");
        budget=capacityBytes;
    }
    private void Check(){if(disposed)throw new ObjectDisposedException("AramAssetCache");}
    private Entry? Find(string id){foreach(var entry in entries)if(entry.Id==id)return entry;return null;}
    public bool Contains(string id){Check();return Find(id)!=null;}
    public Task StoreAsync(string id,byte[] data,CancellationToken cancellationToken=default,int priority=0,bool evictable=true)
    {
        Check();if(id==null)throw new ArgumentNullException("id");if(data==null)throw new ArgumentNullException("data");
        if(data.Length<=0 || data.Length>budget)throw new ArgumentOutOfRangeException("data");
        if(storing)throw new InvalidOperationException("Await the previous cache store before starting another.");
        if(cancellationToken.IsCancellationRequested)return Task.FromCanceled(cancellationToken);
        var work=new StoreWork(this,id,data,cancellationToken,priority,evictable);storing=true;
        try{Scheduler.Post(work.Step);}catch{storing=false;throw;}
        return work.Completion.Task;
    }
    public Task ReadAsync(string id,int sourceOffset,byte[] destination,int destinationOffset,int count,CancellationToken cancellationToken=default)
    {
        Check();var entry=Find(id);if(entry==null)throw new ArgumentException("Asset is not cached.");
        var task=entry.Buffer.ReadAsync(sourceOffset,destination,destinationOffset,count,cancellationToken);
        entries.Remove(entry);entries.Add(entry);return task;
    }
    /// <summary>Fetch a requested range into a new main-RAM buffer for imminent use.</summary>
    public Task<byte[]> PrefetchAsync(string id,int sourceOffset,int count,CancellationToken cancellationToken=default)
    {
        Check();var entry=Find(id);if(entry==null)throw new ArgumentException("Asset is not cached.");
        if(sourceOffset<0 || count<0 || sourceOffset>entry.Buffer.Length || count>entry.Buffer.Length-sourceOffset)throw new ArgumentOutOfRangeException("count");
        var bytes=new byte[count];var task=ReadAsync(id,sourceOffset,bytes,0,count,cancellationToken);var work=new FetchWork(task,bytes);Scheduler.Post(work.Step);return work.Completion.Task;
    }
    public bool Remove(string id)
    {
        Check();if(storing)throw new InvalidOperationException("Await the pending cache store first.");
        var entry=Find(id);if(entry==null)return false;Drop(entry);return true;
    }
    private void Drop(Entry entry){entries.Remove(entry);used-=entry.Bytes;entry.Buffer.Dispose();}
    private bool Evict(Entry? preserve)
    {
        Entry? candidate=null;
        foreach(var entry in entries)if(entry!=preserve && entry.Evictable && (candidate==null || entry.Priority<candidate.Priority))candidate=entry;
        if(candidate==null)return false;Drop(candidate);return true;
    }
    public void Dispose(){if(disposed)return;disposed=true;foreach(var entry in entries)entry.Buffer.Dispose();entries.Clear();used=0;}
    private sealed class FetchWork
    {
        private readonly Task task;private readonly byte[] bytes;
        internal readonly TaskCompletionSource<byte[]> Completion=new TaskCompletionSource<byte[]>();
        internal FetchWork(Task task,byte[] bytes){this.task=task;this.bytes=bytes;}
        internal void Step(){if(!task.IsCompleted){Scheduler.Post(Step);return;}try{task.GetAwaiter().GetResult();Completion.SetResult(bytes);}catch(Exception error){Completion.SetException(error);}}
    }
    private sealed class StoreWork
    {
        private readonly AramAssetCache owner;private readonly string id;private readonly byte[] data;private readonly CancellationToken cancellation;private readonly int priority;private readonly bool evictable;
        private AramBuffer? buffer;private Task? upload;private Entry? previous;private int bytes;
        internal readonly TaskCompletionSource<int> Completion=new TaskCompletionSource<int>();
        internal StoreWork(AramAssetCache owner,string id,byte[] data,CancellationToken cancellation,int priority,bool evictable){this.owner=owner;this.id=id;this.data=data;this.cancellation=cancellation;this.priority=priority;this.evictable=evictable;}
        internal void Step()
        {
            // A pending upload must finish/drain before releasing its allocation.
            if(upload!=null && !upload.IsCompleted){Scheduler.Post(Step);return;}
            try
            {
                owner.Check();cancellation.ThrowIfCancellationRequested();
                if(upload==null)
                {
                    previous=owner.Find(id);bytes=(data.Length+31)/32*32;
                    while(bytes>owner.budget-owner.used)if(!owner.Evict(previous))throw new OutOfMemoryException("Cache budget cannot stage this asset.");
                    while(!Aram.TryAllocate(data.Length,out buffer!))if(!owner.Evict(previous))throw new OutOfMemoryException("No contiguous ARAM block is available.");
                    upload=buffer!.WriteAsync(data,0,0,data.Length,cancellation);Scheduler.Post(Step);return;
                }
                upload.GetAwaiter().GetResult();
                if(previous!=null)owner.Drop(previous);
                owner.entries.Add(new Entry(id,buffer!,bytes,priority,evictable));owner.used+=bytes;buffer=null;owner.storing=false;Completion.SetResult(data.Length);
            }
            catch(Exception error){if(buffer!=null)buffer.Dispose();owner.storing=false;Completion.SetException(error);}
        }
    }
}
