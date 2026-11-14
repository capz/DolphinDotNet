using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Dolphin.Security;
using Scheduler=Dolphin.Threading.Tasks.Scheduler;

internal static class Program
{
    private static CancellationTokenSource? nativeCancellation;

    // The native control-flow fixture invokes this while returning its final
    // mock result. It tests cancellation after collection without depending on
    // timing, threads or any claim about physical entropy quality.
    public static void RequestCancellation()
    {
        if(nativeCancellation!=null)nativeCancellation.Cancel();
    }

    private static void Complete(Task task)
    {
        for(int i=0;i<1000000&&!task.IsCompleted;i++)Scheduler.Pump(16);
        task.GetAwaiter().GetResult();
    }

    private static bool Success(int length,int call)
    {
        var task=PlatformEntropy.GetBytesAsync(length);
        Complete(task);
        var bytes=task.GetAwaiter().GetResult();
        if(bytes.Length!=length||!task.IsCompletedSuccessfully)return false;
        for(int i=0;i<bytes.Length;i++)if(bytes[i]!=(byte)((i+call*17)%251+1))return false;
        Array.Clear(bytes,0,bytes.Length);
        return true;
    }

    private static bool Fault(int kind)
    {
        var task=PlatformEntropy.GetBytesAsync(64);
        bool caught=false;
        try { Complete(task); }
        catch(IOException) { caught=kind==0||kind==4; }
        catch(OutOfMemoryException) { caught=kind==1; }
        catch(TimeoutException) { caught=kind==2; }
        catch(InvalidOperationException) { caught=kind==3; }
        return caught&&task.IsFaulted;
    }

    public static int Main()
    {
        RequestCancellation(); // Keep the fixture hook reachable; no source yet.
        try { PlatformEntropy.GetBytesAsync(47);return 1; }
        catch(ArgumentOutOfRangeException) {}
        try { PlatformEntropy.GetBytesAsync(65);return 2; }
        catch(ArgumentOutOfRangeException) {}
        using(var cancel=new CancellationTokenSource()) {
            cancel.Cancel();var task=PlatformEntropy.GetBytesAsync(64,cancel.Token);
            try { Complete(task);return 3; }
            catch(OperationCanceledException) {}
            if(!task.IsCanceled)return 4;
        }
        if(!Success(48,1)||!Success(64,2))return 5;
        for(int kind=0;kind<5;kind++)if(!Fault(kind))return 10+kind;
        using(var cancel=new CancellationTokenSource()) {
            nativeCancellation=cancel;
            var task=PlatformEntropy.GetBytesAsync(64,cancel.Token);
            try { Complete(task);return 20; }
            catch(OperationCanceledException) {}
            nativeCancellation=null;
            if(!task.IsCanceled||!cancel.IsCancellationRequested)return 21;
        }
        if(!Success(64,9))return 22;
        Console.WriteLine("Entropy facade: sizes, success, typed faults, cancellation and output cleanup passed");
        return 0;
    }
}
