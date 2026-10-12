using System;
using Dolphin.Memory;
using System.Threading;
using System.Threading.Tasks;
using Scheduler = Dolphin.Threading.Tasks.Scheduler;
class Program
{
    static int Main()
    {
        if(!Aram.Initialize(16*1024*1024-0x4000-32768) || !Aram.IsAvailable)return 1;
        if(Aram.Statistics.CapacityBytes!=32768 || !Aram.Initialize(16*1024*1024-0x4000-32768) || Aram.Initialize(0))return 2;
        var src=new byte[9001];for(var i=0;i<src.Length;i++)src[i]=(byte)(i*3);
        var buffer=Aram.Allocate(9020);var task=buffer.WriteAsync(src,0,3,src.Length);if(task.IsCompleted)return 3;
        task.GetAwaiter().GetResult();var dst=new byte[9001];buffer.ReadAsync(3,dst,0,dst.Length).GetAwaiter().GetResult();
        for(var i=0;i<dst.Length;i++)if(dst[i]!=src[i])return 4;
        // Submission ordering and deferred disposal, including a queued read.
        src[0]=77;var write=buffer.WriteAsync(src,0,3,1);var read=buffer.ReadAsync(3,dst,0,1);buffer.Dispose();
        try{buffer.ReadAsync(0,dst,0,1);return 5;}catch(ObjectDisposedException){}
        if(Aram.Statistics.AllocationCount!=1)return 6;
        read.GetAwaiter().GetResult();if(!write.IsCompleted || dst[0]!=77 || Aram.Statistics.AllocationCount!=0)return 7;
        var cancel=new CancellationTokenSource();cancel.Cancel();buffer=Aram.Allocate(12000);
        var canceled=buffer.WriteAsync(src,0,0,src.Length,cancel.Token);if(!canceled.IsCanceled)return 8;
        cancel=new CancellationTokenSource();task=buffer.WriteAsync(src,0,0,src.Length,cancel.Token);Scheduler.Pump();cancel.Cancel();
        try{task.GetAwaiter().GetResult();return 9;}catch(OperationCanceledException){}
        buffer.Dispose();if(Aram.Statistics.AllocationCount!=0)return 10;
        try{Aram.Allocate(40000);return 11;}catch(OutOfMemoryException){}
        using(var cache=new AramAssetCache(8192))
        {
            var small=new byte[3000];small[0]=33;
            cache.StoreAsync("low",small,default,-1).GetAwaiter().GetResult();
            cache.StoreAsync("high",small,default,5).GetAwaiter().GetResult();
            cache.StoreAsync("new",small).GetAwaiter().GetResult();
            if(cache.Contains("low") || !cache.Contains("high") || !cache.Contains("new"))return 12;
            var prefetched=cache.PrefetchAsync("new",0,4).GetAwaiter().GetResult();if(prefetched[0]!=33)return 13;
            // Cancel replacement without exposing a partially uploaded entry.
            cancel=new CancellationTokenSource();small[0]=99;
            task=cache.StoreAsync("new",small,cancel.Token);Scheduler.Pump();cancel.Cancel();
            try{task.GetAwaiter().GetResult();return 14;}catch(OperationCanceledException){}
            prefetched=cache.PrefetchAsync("new",0,4).GetAwaiter().GetResult();if(prefetched[0]!=33)return 15;
            if(!cache.Remove("new"))return 16;
        }
        if(Aram.Statistics.AllocationCount!=0)return 17;
        var owner=new AramAssetCache(8192);task=owner.StoreAsync("dispose",new byte[6000]);Scheduler.Pump();owner.Dispose();
        try{task.GetAwaiter().GetResult();return 18;}catch(ObjectDisposedException){}
        if(Aram.Statistics.AllocationCount!=0)return 19;
        using(var pinned=new AramAssetCache(4096))
        {
            pinned.StoreAsync("keep",new byte[3000],default,0,false).GetAwaiter().GetResult();
            try{pinned.StoreAsync("other",new byte[3000]).GetAwaiter().GetResult();return 20;}catch(OutOfMemoryException){}
            if(!pinned.Contains("keep"))return 21;
        }
        buffer=Aram.Allocate(32);var one=new byte[]{9};var pending=new Task[128];
        for(var i=0;i<128;i++)pending[i]=buffer.WriteAsync(one,0,0,1);
        try{buffer.WriteAsync(one,0,0,1);return 22;}catch(InvalidOperationException){}
        buffer.Dispose();Task.WhenAll(pending).GetAwaiter().GetResult();
        if(Aram.Statistics.AllocationCount!=0)return 23;
        return 0;
    }
}
