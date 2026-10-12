using System;
using System.Threading;
using System.Threading.Tasks;
using Scheduler = Dolphin.Threading.Tasks.Scheduler;
using NativeTasks = Dolphin.Threading.Tasks;

internal static class Program
{
    private static int phase;
    private static async Task<int> NestedAsync(string retained)
    {
        await Task.Yield();
        phase = 1;
        var value = await Task.Run(() => 40).ConfigureAwait(false);
        await Task.Yield();
        return value + retained.Length;
    }

    private static async Task FaultAsync()
    {
        await Task.Yield();
        throw new InvalidOperationException("awaited fault");
    }

    private static async Task CatchFaultAsync()
    {
        try { await FaultAsync(); }
        catch (InvalidOperationException) { phase = 2; }
        finally { phase = phase + 1; }
    }

    private static async Task CancelAsync(CancellationToken token)
    {
        await Task.Yield();
        token.ThrowIfCancellationRequested();
    }

    private static async Task<int> AwaitSourceAsync(Task<int> source)
    {
        return await source.ConfigureAwait(false);
    }

    private static async Task<int> AwaitPlatformAsync(NativeTasks.Task<int> source)
    {
        return await source.ConfigureAwait(false);
    }

    private static void Drain(Task task)
    {
        for (var turns = 0; turns < 1000 && !task.IsCompleted; turns++) Scheduler.Pump(1);
        if (!task.IsCompleted) throw new InvalidOperationException("Async test did not complete.");
    }

    private static int Main()
    {
        // The string and state machine must survive allocations and collections
        // across multiple deferred continuations, with bounded frame pumping.
        var nested = NestedAsync(string.Concat("a", "b"));
        if (nested.IsCompleted || phase != 0) return 1;
        Scheduler.Pump(1);
        if (nested.IsCompleted || phase != 1) return 2;
        Drain(nested);
        if (nested.GetAwaiter().GetResult() != 42) return 3;

        var caught = CatchFaultAsync();
        Drain(caught);
        caught.GetAwaiter().GetResult();
        if (phase != 3 || !caught.IsCompletedSuccessfully) return 4;
        var fault = FaultAsync();
        Drain(fault);
        try { fault.GetAwaiter().GetResult(); return 5; }
        catch (InvalidOperationException) { }
        if (!fault.IsFaulted) return 6;

        using var cancellation = new CancellationTokenSource();
        var canceled = CancelAsync(cancellation.Token);
        cancellation.Cancel();
        Drain(canceled);
        try { canceled.GetAwaiter().GetResult(); return 7; }
        catch (OperationCanceledException) { }
        if (!canceled.IsCanceled) return 8;

        var first = new TaskCompletionSource<int>();
        var second = new TaskCompletionSource<int>();
        var resumed = AwaitSourceAsync(first.Task);
        var any = Task.WhenAny(new Task[] { first.Task, second.Task });
        var all = Task.WhenAll(new Task[] { first.Task, second.Task });
        if (resumed.IsCompleted || any.IsCompleted || all.IsCompleted) return 9;
        first.SetResult(42);
        Drain(resumed); Drain(any);
        if (resumed.GetAwaiter().GetResult() != 42 || any.GetAwaiter().GetResult() != first.Task || all.IsCompleted) return 10;
        second.SetResult(7);
        Drain(all); all.GetAwaiter().GetResult();

        // Deliberately cross the CLR-visible System/Dolphin boundary by awaiting,
        // never by casting or assigning unrelated Task classes.
        var native = new NativeTasks.TaskCompletionSource<int>();
        var bridged = AwaitPlatformAsync(native.Task);
        if (bridged.IsCompleted) return 11;
        native.SetResult(19);
        Drain(bridged);
        if (bridged.GetAwaiter().GetResult() != 19) return 12;
        return 0;
    }
}
