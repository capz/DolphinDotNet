using System;
using System.Threading;
using System.Threading.Tasks;
using Dolphin.Graphics;
using Dolphin.Memory;

internal static class Program
{
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static async Task<int> Main()
    {
        Check(Aram.Initialize(2*1024*1024),"ARAM initialization.");
        var input=new byte[65536];for(int i=0;i<input.Length;i++)input[i]=(byte)(i*17+3);
        var output=new byte[input.Length];
        using(var source=NativeBuffer.Allocate(input.Length))
        using(var destination=NativeBuffer.Allocate(input.Length))
        using(var aram=Aram.Allocate(input.Length))
        {
            source.Write(input);
            // The higher-priority read aliases a preceding background upload.
            // Dependency ordering must preserve the completed snapshot.
            var write=AramTransferQueue.CopyToAramAsync(source,0,aram,0,input.Length,AramTransferPriority.Background);
            var read=AramTransferQueue.CopyToMainAsync(aram,0,destination,0,input.Length,AramTransferPriority.Demand);
            await read;await write;
            destination.Read(output);
            for(int i=0;i<input.Length;i++)Check(input[i]==output[i],"Native broker roundtrip/dependency order.");
            var cancel=new CancellationTokenSource();
            var canceled=AramTransferQueue.CopyToMainAsync(aram,0,destination,0,input.Length,AramTransferPriority.Prefetch,cancel.Token);
            cancel.Cancel();
            try{await canceled;throw new InvalidOperationException("Canceled ticket completed successfully.");}
            catch(OperationCanceledException){}
            try{await AramTransferQueue.CopyToMainAsync(aram,0,destination,0,32,(AramTransferPriority)3);throw new InvalidOperationException("Invalid priority accepted.");}
            catch(ArgumentOutOfRangeException){}
            // Old signatures remain usable and retain their ordered data contract.
            await AramTransferQueue.CopyToAramAsync(source,0,aram,0,32);
            await AramTransferQueue.CopyToMainAsync(aram,0,destination,0,32);
        }
        Check(Aram.Statistics.AllocationCount==0,"Transfer owners leaked.");
        var retained=NativeBuffer.Allocate(65536);var stored=Aram.Allocate(65536);
        retained.Write(input);
        var pending=AramTransferQueue.CopyToAramAsync(retained,0,stored,0,65536);
        retained.Dispose();stored.Dispose();
        Check(Aram.Statistics.AllocationCount==1,"Queued transfer released closed storage too early.");
        await pending;
        Check(Aram.Statistics.AllocationCount==0,"Completed disposed owners were not reclaimed.");
        return 0;
    }
}
