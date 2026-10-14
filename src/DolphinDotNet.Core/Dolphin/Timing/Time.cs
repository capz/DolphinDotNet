using System;
using System.Threading;
using System.Threading.Tasks;

namespace Dolphin.Timing;
public static class Time
{
    public static Task DelayAsync(int milliseconds) => DelayAsync(milliseconds,default);
    /// <summary>Runtime-dispatched delay. -1 waits indefinitely until cancellation; zero completes immediately.</summary>
    public static Task DelayAsync(int milliseconds, CancellationToken cancellationToken)
    {
        if (milliseconds < -1) throw new ArgumentOutOfRangeException("milliseconds");
        if (cancellationToken.IsCancellationRequested)
        {
            var canceled=new TaskCompletionSource<int>();
            canceled.SetCanceled();
            return canceled.Task;
        }
        if (milliseconds==0) return Task.CompletedTask;
        var timer=new Timer(null,milliseconds,0,cancellationToken);
        return timer.Completion;
    }
}
