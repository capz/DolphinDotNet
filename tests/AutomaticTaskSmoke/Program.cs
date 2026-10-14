using System;
using System.Threading;
using System.Threading.Tasks;
using Scheduler = Dolphin.Threading.Tasks.Scheduler;

internal static class Program
{
    private static async Task<int> Main()
    {
        if(!Scheduler.IsApplicationThread)return 1;
        var retained=string.Concat("application-", "root");
        await Task.Yield();
        var result=await Task.Run(()=>
        {
            if(Scheduler.IsApplicationThread)throw new InvalidOperationException("Wrong executor.");
            var count=0;
            for(var i=0;i<100;i++)count+=string.Concat("worker-", "allocation").Length;
            return count;
        });
        if(result!=1700||retained!="application-root"||!Scheduler.IsApplicationThread)return 2;
        var source=new TaskCompletionSource<int>();
        var switched=SwitchAsync(source.Task);
        source.SetResult(1);
        if(await switched!=1||!Scheduler.IsApplicationThread)return 3;
        var first=Task.Run(()=>{});
        var second=Task.Run(()=>{});
        await Task.WhenAll(new Task[]{first,second});
        try { await Task.Run(()=>{throw new InvalidOperationException("fault");});return 4; }
        catch(InvalidOperationException) { }
        using(var cancellation=new CancellationTokenSource())
        {
            cancellation.Cancel();
            try { await Task.Run(()=>{},cancellation.Token);return 5; }
            catch(OperationCanceledException) { }
        }
        return Scheduler.IsApplicationThread?0:6;
    }
    private static async Task<int> SwitchAsync(Task<int> task)
    {
        await task.ConfigureAwait(false);
        return Scheduler.IsApplicationThread?0:1;
    }
}
