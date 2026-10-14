using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Scheduler = Dolphin.Threading.Tasks.Scheduler;

namespace Dolphin.Timing;

/// <summary>A disposable one-shot or periodic callback, posted to the executor captured at scheduling.</summary>
public sealed class Timer : IDisposable
{
    private readonly Action? callback;
    private readonly Action dispatch;
    private readonly int context;
    private readonly long period;
    private readonly CancellationToken cancellation;
    private readonly TaskCompletionSource<int> completion=new TaskCompletionSource<int>();
    private long due;
    private bool disposed, queued, executing;
    public Task Completion => completion.Task;
    internal Timer(Action? callback, int delay, int period, CancellationToken cancellation)
    {
        this.callback=callback; this.period=(long)period*1000; this.cancellation=cancellation;
        context=Scheduler.Context; dispatch=Dispatch;
        due=delay<0 ? -1 : Clock.Timestamp+(long)delay*1000;
        TimingRuntime.Add(this);
    }
    public static Timer Schedule(Action callback, int dueMilliseconds, int periodMilliseconds=0)
        => Schedule(callback,dueMilliseconds,periodMilliseconds,default);
    public static Timer Schedule(Action callback, int dueMilliseconds, int periodMilliseconds, CancellationToken cancellationToken)
    {
        if (callback==null) throw new ArgumentNullException("callback");
        if (dueMilliseconds<0) throw new ArgumentOutOfRangeException("dueMilliseconds");
        if (periodMilliseconds<0) throw new ArgumentOutOfRangeException("periodMilliseconds");
        return new Timer(callback,dueMilliseconds,periodMilliseconds,cancellationToken);
    }
    internal void Poll(long now)
    {
        if (disposed) return;
        if (cancellation.IsCancellationRequested) { Cancel(); return; }
        if (due<0 || now<due) return;
        if (callback==null) { Close(); completion.SetResult(0); return; }
        if (period>0) due+=((now-due)/period+1)*period; // Coalesce missed intervals.
        if (queued || executing) return;
        queued=true;
        Scheduler.Post(dispatch,context);
    }
    private void Dispatch()
    {
        if (disposed) return;
        if (cancellation.IsCancellationRequested) { Cancel(); return; }
        queued=false;
        executing=true;
        try { callback!(); }
        catch (Exception error)
        {
            if (!disposed) Close();
            if (!completion.Task.IsCompleted) completion.SetException(error);
            return;
        }
        finally { executing=false; }
        if (!disposed && period==0) Close();
        if (disposed && !completion.Task.IsCompleted) completion.SetResult(0);
    }
    private void Close() { disposed=true; TimingRuntime.Remove(this); }
    internal void Cancel() { if (disposed) return; Close(); completion.SetCanceled(); }
    /// <summary>Suppresses queued callbacks. Callback timers complete normally; pending delays cancel.</summary>
    public void Dispose()
    {
        if (disposed) return;
        Close();
        if (executing) return; // A callback can dispose itself; settle after it returns or faults.
        if (callback==null) completion.SetCanceled(); else completion.SetResult(0);
    }
}

internal static class TimingRuntime
{
    private static readonly List<Timer> timers=new List<Timer>();
    internal static int Pending => timers.Count;
    internal static void Add(Timer timer)
    {
        if (timers.Count>=64) throw new InvalidOperationException("Timing operation limit reached.");
        timers.Add(timer); Dolphin.Threading.Tasks.NativeAsync.AsyncWake();
    }
    internal static void Remove(Timer timer) { timers.Remove(timer); }
    internal static void Pump()
    {
        long now=Clock.Timestamp;
        for (int i=timers.Count-1;i>=0;i--) timers[i].Poll(now);
    }
    internal static void Stop() { while (timers.Count>0) timers[timers.Count-1].Cancel(); }
}
