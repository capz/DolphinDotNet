using System;
using System.Threading;
using System.Threading.Tasks;
using Dolphin.Timing;
using Timer = Dolphin.Timing.Timer;
using Scheduler = Dolphin.Threading.Tasks.Scheduler;
internal static class Program
{
    private static async Task<int> Main()
    {
        long start=Clock.Timestamp;
        var watch=Stopwatch.StartNew();
        await Task.Delay(15);
        if (!Scheduler.IsApplicationThread || Clock.Timestamp-start<15000 || watch.ElapsedMilliseconds<15) return 1;
        watch.Stop(); long stopped=watch.ElapsedTicks;
        await Time.DelayAsync(5);
        if (watch.ElapsedTicks!=stopped || watch.IsRunning) return 2;
        watch.Start(); await Time.DelayAsync(5); watch.Stop();
        if (watch.ElapsedTicks<=stopped) return 3;
        watch.Reset(); if (watch.ElapsedTicks!=0) return 4;
        watch.Restart(); if (!watch.IsRunning) return 5; watch.Stop();
        var frame=new FrameClock { MaximumDeltaSeconds=0.005f };
        if (frame.Tick()!=0) return 6;
        await Task.Delay(10);
        if (frame.Tick()!=0.005f || frame.TotalSeconds<0.0049) return 7;
        frame.Pause(); await Task.Delay(2); if (frame.Tick()!=0 || frame.DeltaSeconds!=0) return 8;
        frame.Resume(); if (frame.Tick()!=0) return 9;
        frame.Reset(); if (frame.TotalSeconds!=0 || frame.Tick()!=0) return 10;
        if (!Task.Delay(0).IsCompleted) return 11;
        try { _ = Time.DelayAsync(-2); return 12; } catch (ArgumentOutOfRangeException) { }
        try { Clock.ElapsedSeconds(2,1); return 13; } catch (ArgumentOutOfRangeException) { }
        using(var cancellation=new CancellationTokenSource())
        {
            cancellation.Cancel();
            var canceled=Task.Delay(1,cancellation.Token);
            if (!canceled.IsCanceled) return 14;
            try { await canceled; return 15; } catch (OperationCanceledException) { }
        }
        using(var cancellation=new CancellationTokenSource())
        {
            var pending=Task.Delay(-1,cancellation.Token);
            await Task.Delay(2); cancellation.Cancel();
            try { await pending; return 16; } catch (OperationCanceledException) { }
        }
        int ticks=0;
        using(var timer=Timer.Schedule(()=> {
            if (!Scheduler.IsApplicationThread) throw new InvalidOperationException("Wrong timer executor.");
            ticks++;
        },0,2))
        {
            await Task.Delay(15);
            if (ticks==0) return 17;
            timer.Dispose(); int stoppedTicks=ticks;
            await timer.Completion; await Task.Delay(4);
            if (ticks!=stoppedTicks) return 18;
        }
        var once=Timer.Schedule(()=>ticks++,1);
        await once.Completion; int onceTicks=ticks;
        await Task.Delay(3); if (ticks!=onceTicks) return 19;
        var fault=Timer.Schedule(()=>{throw new InvalidOperationException("timer fault");},0);
        try { await fault.Completion; return 20; } catch (InvalidOperationException) { }
        Timer? self=null;
        self=Timer.Schedule(()=> { self!.Dispose(); throw new InvalidOperationException("fault after self-dispose"); },1);
        try { await self.Completion; return 25; } catch (InvalidOperationException) { }
        bool background=false;
        var workerTimer=await Task.Run(()=>Timer.Schedule(()=>background=!Scheduler.IsApplicationThread,0));
        await workerTimer.Completion; if (!background || !Scheduler.IsApplicationThread) return 21;
        int active=0, blockingTicks=0;
        var blocking=await Task.Run(()=>Timer.Schedule(()=> {
            active++;
            if (active!=1) throw new InvalidOperationException("Overlapping periodic callback.");
            Task.Delay(2).Wait();
            blockingTicks++; active--;
        },0,1));
        await Task.Delay(10);
        blocking.Dispose(); await blocking.Completion;
        if (active!=0 || blockingTicks==0) return 26;
        using(var cancellation=new CancellationTokenSource())
        {
            var waiters=new Task[64];
            for(int i=0;i<waiters.Length;i++) waiters[i]=Task.Delay(-1,cancellation.Token);
            try { _ = Task.Delay(-1); return 23; } catch(InvalidOperationException) { }
            cancellation.Cancel();
            try { await Task.WhenAll(waiters); return 24; } catch(OperationCanceledException) { }
        }
        await Task.Delay(1).ConfigureAwait(false);
        if (Scheduler.IsApplicationThread) return 22;
        // Shutdown must cancel outstanding timing work and join the service.
        Timer.Schedule(()=>{throw new Exception("Should not run after exit.");},100000,100000);
        _ = Time.DelayAsync(-1);
        return 0;
    }
}
