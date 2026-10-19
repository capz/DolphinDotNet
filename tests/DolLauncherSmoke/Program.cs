using System;
using System.Threading;
using Dolphin.IO;

internal static class Program
{
    private static int Main()
    {
        try { DolLauncher.Prepare((byte[])null!); return 1; } catch (ArgumentNullException) { }
        try { DolLauncher.Prepare(new byte[255]); return 2; } catch (ArgumentException) { }
        try { DolLauncher.RequestLaunch(null!); return 3; } catch (ArgumentNullException) { }
        var cancel = new CancellationTokenSource();cancel.Cancel();
        var canceled = DolLauncher.PrepareAsync("sd:/next.dol", cancel.Token);
        try { canceled.GetAwaiter().GetResult(); return 4; } catch (OperationCanceledException) { }
        if (!canceled.IsCanceled) return 5;
        var duringRead = new CancellationTokenSource();
        var interrupted = DolLauncher.PrepareAsync("sd:/next.dol", duringRead.Token);
        Dolphin.Threading.Tasks.Scheduler.Post(duringRead.Cancel);
        try { interrupted.GetAwaiter().GetResult(); return 9; } catch (OperationCanceledException) { }
        if (!interrupted.IsCanceled) return 10;
        using (var prepared = DolLauncher.Prepare("sd:/next.dol"))
        {
            if ((prepared.Info.EntryPoint >> 16) != 0x8000 || (prepared.Info.EntryPoint & 0xffff) != 0x4000 || prepared.Info.SectionCount != 1 || prepared.Info.FileSize != 9000) return 6;
        }
        var disposed = DolLauncher.Prepare("sd:/next.dol");disposed.Dispose();disposed.Dispose();
        try { DolLauncher.RequestLaunch(disposed); return 7; } catch (ObjectDisposedException) { }
        using (var prepared = DolLauncher.PrepareAsync("sd:/next.dol").GetAwaiter().GetResult())
        {
            try
            {
                DolLauncher.RequestLaunch(prepared);

            }
            finally
            {
                // Observable after request but before handoff. Bootstrap must
                // retain the queued image despite Dispose in this using block.
                var cleanup = DolLauncher.Prepare("sd:/next.dol");
                cleanup.Dispose();
            }
        }
        return 0;
    }
}
