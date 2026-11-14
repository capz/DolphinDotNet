using System;
using System.Threading;
using System.Threading.Tasks;
using Dolphin.Security;

static class Program
{
    static async Task Main()
    {
        foreach(int count in new[]{int.MinValue,0,47,65,int.MaxValue}) {
            bool rejected=false;
            try {await PlatformEntropy.GetBytesAsync(count);}
            catch(ArgumentOutOfRangeException){rejected=true;}
            if(!rejected)throw new Exception("Invalid seed size accepted.");
        }
        using var cancellation=new CancellationTokenSource();
        cancellation.Cancel();
        bool canceled=false;
        try {await PlatformEntropy.GetBytesAsync(48,cancellation.Token);}
        catch(OperationCanceledException){canceled=true;}
        if(!canceled)throw new Exception("Pre-cancellation was not observed.");
        bool unsupported=false;
        try {await PlatformEntropy.GetBytesAsync(64);}
        catch(PlatformNotSupportedException){unsupported=true;}
        if(!unsupported)throw new Exception("Host unexpectedly fabricated a console seed.");
        Console.WriteLine("Platform entropy managed bounds, cancellation, and unsupported-host behavior passed.");
    }
}
