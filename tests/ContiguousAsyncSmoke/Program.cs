using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Dolphin.Storage;
using Scheduler=Dolphin.Threading.Tasks.Scheduler;

internal static class Program
{
    private static void Complete(Task task)
    {
        for(int i=0;i<1000&&!task.IsCompleted;i++)Scheduler.Pump(16);
        task.GetAwaiter().GetResult();
    }
    private static int Main()
    {
        using(var writer=Storage.CreateContiguousFile("gcloader:/test.bin",65537)){
            var done=writer.CompleteAsync();
            if(done.IsCompleted)return 1;
            try{writer.Write(new byte[1],0,1);return 2;}catch(InvalidOperationException){}
            try{writer.Complete();return 3;}catch(InvalidOperationException){}
            try{Complete(writer.CompleteAsync());return 4;}catch(InvalidOperationException){}
            Complete(done);
            try{writer.Write(new byte[1],0,1);return 5;}catch(ObjectDisposedException){}
        }
        using(var writer=Storage.CreateContiguousFile("gcloader:/cancel.bin",65537))
        using(var cancel=new CancellationTokenSource()){
            var done=writer.CompleteAsync(cancel.Token);cancel.Cancel();
            try{Complete(done);return 6;}catch(OperationCanceledException){}
            if(!done.IsCanceled)return 7;
        }
        using(var writer=Storage.CreateContiguousFile("gcloader:/pre-cancel.bin",65537))
        using(var cancel=new CancellationTokenSource()){
            cancel.Cancel();
            try{Complete(writer.CompleteAsync(cancel.Token));return 8;}catch(OperationCanceledException){}
        }
        using(var writer=Storage.CreateContiguousFile("gcloader:/disposed.bin",65537)){
            var done=writer.CompleteAsync();writer.Dispose();
            try{Complete(done);return 9;}catch(ObjectDisposedException){}
        }
        using(var writer=Storage.CreateContiguousFile("gcloader:/fault.bin",99)){
            try{Complete(writer.CompleteAsync());return 10;}catch(IOException){}
        }
        Console.WriteLine("Contiguous completion: yielding, cancellation, disposal, exclusivity and native failure passed");
        return 0;
    }
}
