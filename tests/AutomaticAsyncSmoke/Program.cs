using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Dolphin.Memory;
using AramStream = Dolphin.IO.AramStream;
using NetworkRuntime = Dolphin.Network.NetworkRuntime;
using NetworkOptions = Dolphin.Network.NetworkOptions;
using Scheduler = Dolphin.Threading.Tasks.Scheduler;

internal static class Program
{
    private static async Task<int> Main()
    {
        try
        {
            if(!Scheduler.IsApplicationThread)return 1;
            // Suspend main's live string/exception frames while background code
            // allocates under GC stress and completes a task from another thread.
            var retained=string.Concat("live-", "application");
            var background=Task.Run(()=>
            {
                if(Scheduler.IsApplicationThread)throw new InvalidOperationException("Wrong executor.");
                var value=0;
                for(var i=0;i<200;i++)value+=string.Concat("worker", "-allocation").Length;
                return value;
            });
            if(await background!=3400||retained!="live-application"||!Scheduler.IsApplicationThread)return 2;
            try { await Task.Run(()=>{throw new InvalidOperationException("worker fault");});return 3; }
            catch(InvalidOperationException) { }
            if(!Scheduler.IsApplicationThread)return 4;
            using(var cancel=new CancellationTokenSource())
            {
                cancel.Cancel();
                try { await Task.Run(()=>{},cancel.Token);return 5; }
                catch(OperationCanceledException) { }
            }
            // A synchronous application loop also progresses without pumps.
            var completion=new TaskCompletionSource<int>();
            var work=Task.Run(()=>completion.SetResult(23));
            var spins=0;
            while(!completion.Task.IsCompleted)spins++;
            if(await completion.Task!=23)return 6;
            await work;
            await Task.Yield();
            if(!Scheduler.IsApplicationThread)return 7;
            // Cancellation while the service thread has live managed frames.
            using(var cancel=new CancellationTokenSource())
            {
                var started=new TaskCompletionSource<int>();
                var canceled=Task.Run(()=>
                {
                    var live=string.Concat("worker-", "root");
                    started.SetResult(1);
                    try { while(true)cancel.Token.ThrowIfCancellationRequested(); }
                    finally { if(live!="worker-root")throw new InvalidOperationException("Lost worker root."); }
                });
                await started.Task;
                // Collect on the application executor while the worker is paused.
                for(var i=0;i<100;i++)retained=string.Concat("application-", "allocation");
                cancel.Cancel();
                try { await canceled;return 13; }
                catch(OperationCanceledException) { }
            }
            // Preserve native EH frames and the original exception across waits.
            try { RethrowAfterWork(Task.Run(()=>string.Concat("collect-", "while-main-catches")));return 14; }
            catch(InvalidOperationException error) { if(error.Message!="original main exception")return 15; }
            await NetworkRuntime.StartAsync(new NetworkOptions {
                DnsServers=new[]{Dolphin.Network.IPAddress.Loopback},DnsPort=15353
            },default);
            if(!Aram.Initialize())return 8;
            using(var http=new HttpClient())
            using(var response=await http.GetAsync("http://config.test:18081/large",HttpCompletionOption.ResponseHeadersRead))
            using(var body=await response.Content.ReadAsStreamAsync())
            using(var stored=new AramStream(20000))
            {
                response.EnsureSuccessStatusCode();
                await body.CopyToAsync(stored,4096);
                if(stored.Length!=12001)return 9;
                stored.Position=0;
                using(var memory=new MemoryStream())
                {
                    await stored.CopyToAsync(memory,4096);
                    var bytes=memory.ToArray();
                    for(var i=0;i<bytes.Length;i++)if(bytes[i]!=(byte)(i%251))return 10;
                }
            }
            if(Aram.Statistics.AllocationCount!=0)return 11;
            // Force a deferred ConfigureAwait(false), even if Task.Run is fast.
            var source=new TaskCompletionSource<int>();
            var switched=SwitchAsync(source.Task);
            source.SetResult(1);
            if(await switched!=1||!Scheduler.IsApplicationThread)return 12;
            // Returning Main must cancel/unwind an active background callback
            // and join it, rather than hanging or leaving a managed thread alive.
            var shutdownStarted=new TaskCompletionSource<int>();
            var abandoned=Task.Run(()=>
            {
                using(var owned=Aram.Allocate(32))
                {
                    shutdownStarted.SetResult(1);
                    while(true){}
                }
            });
            await shutdownStarted.Task;
            return 0;
        }
        catch(Exception) { return 99; }
        finally { if(NetworkRuntime.IsReady)NetworkRuntime.Stop(); }
    }
    private static async Task<int> SwitchAsync(Task<int> task)
    {
        await task.ConfigureAwait(false);
        return Scheduler.IsApplicationThread?0:1;
    }
    private static void RethrowAfterWork(Task task)
    {
        try { throw new InvalidOperationException("original main exception"); }
        catch { task.GetAwaiter().GetResult();throw; }
    }
}
