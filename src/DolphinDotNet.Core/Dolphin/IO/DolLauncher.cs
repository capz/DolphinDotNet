using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Scheduler = Dolphin.Threading.Tasks.Scheduler;

namespace Dolphin.IO;

/// <summary>Metadata for a validated GameCube DOL. Addresses use cached MEM1.</summary>
public sealed class DolInfo
{
    public uint EntryPoint { get; }
    public int FileSize { get; }
    public int SectionCount { get; }
    public int BssSize { get; }
    internal DolInfo(int token)
    {
        EntryPoint = (uint)NativeDol.DolInfo(token, 0);
        FileSize = NativeDol.DolInfo(token, 1);
        SectionCount = NativeDol.DolInfo(token, 2);
        BssSize = NativeDol.DolInfo(token, 3);
    }
}

/// <summary>Owns a native copy of a DOL until disposed or transferred to bootstrap.</summary>
public sealed class PreparedDol : IDisposable
{
    private int token;
    public DolInfo Info { get; }
    internal PreparedDol(int handle) { token = handle; Info = new DolInfo(handle); }
    internal void Request()
    {
        if (token == 0) throw new ObjectDisposedException("PreparedDol");
        DolLauncher.Check(NativeDol.DolRequest(token));
        token = 0; // Bootstrap now owns the image; disposing this wrapper is safe.
    }
    public void Dispose()
    {
        if (token != 0) { NativeDol.DolDispose(token); token = 0; }
    }
}

/// <summary>Prepares a GameCube executable and queues replacement of the current app.</summary>
public static class DolLauncher
{
    /// <summary>Copies and validates the image into native memory. The input may be reused afterwards.</summary>
    public static PreparedDol Prepare(byte[] image)
    {
        if (image == null) throw new ArgumentNullException("image");
        var token = Check(NativeDol.DolPrepare(image));
        return Wrap(token);
    }

    /// <summary>Reads a DOL from an already-mounted filesystem without using the managed heap for its contents.</summary>
    public static PreparedDol Prepare(string path)
    {
        if (path == null) throw new ArgumentNullException("path");
        var token = Check(NativeDol.DolBegin(path));
        try
        {
            while (Check(NativeDol.DolStep(token)) == 0) { }
            return Wrap(token);
        }
        catch { NativeDol.DolDispose(token); throw; }
    }

    /// <summary>Reads at most 4096 bytes per scheduled step. Cancellation releases the file and native staging allocation.</summary>
    public static Task<PreparedDol> PrepareAsync(string path, CancellationToken cancellationToken = default)
    {
        if (path == null) throw new ArgumentNullException("path");
        var work = new PrepareWork(path, cancellationToken);
        Scheduler.Post(work.Step);
        return work.Completion.Task;
    }

    /// <summary>
    /// Transfers ownership and requests launch after Main returns successfully.
    /// Return from Main to run finally/using cleanup. A failing Main cancels launch.
    /// One launch may be queued. Call on the application thread.
    /// </summary>
    public static void RequestLaunch(PreparedDol executable)
    {
        if (executable == null) throw new ArgumentNullException("executable");
        executable.Request();
    }

    private static PreparedDol Wrap(int token)
    {
        try { return new PreparedDol(token); }
        catch { NativeDol.DolDispose(token); throw; }
    }
    internal static int Check(int result)
    {
        if (result >= 0) return result;
        if (result == -1) throw new ArgumentException("Invalid or unsupported DOL layout. Requires non-overlapping cached MEM1 sections and an executable entry point.");
        if (result == -2) throw new OutOfMemoryException("Not enough native memory to stage the DOL.");
        if (result == -3) throw new InvalidOperationException("The DOL would overwrite its staging allocation. This layout needs an ARAM/relocating loader.");
        if (result == -4) throw new IOException("Could not read the complete DOL.");
        if (result == -5) throw new InvalidOperationException("Too many prepared DOLs. Dispose an unused executable first.");
        if (result == -6) throw new ObjectDisposedException("PreparedDol");
        if (result == -7) throw new InvalidOperationException("A DOL launch is already queued.");
        throw new PlatformNotSupportedException("DOL launching requires a DolphinDotNet GameCube AOT build.");
    }
    private sealed class PrepareWork
    {
        private readonly string path;
        private readonly CancellationToken cancellation;
        private int token;
        internal readonly TaskCompletionSource<PreparedDol> Completion = new TaskCompletionSource<PreparedDol>();
        internal PrepareWork(string path, CancellationToken cancellation) { this.path = path; this.cancellation = cancellation; }
        internal void Step()
        {
            try
            {
                cancellation.ThrowIfCancellationRequested();
                if (token == 0) token = Check(NativeDol.DolBegin(path));
                if (Check(NativeDol.DolStep(token)) == 0) { Scheduler.Post(Step); return; }
                cancellation.ThrowIfCancellationRequested();
                var result = Wrap(token);
                token = 0;
                Completion.SetResult(result);
            }
            catch (OperationCanceledException)
            {
                if (token != 0) NativeDol.DolDispose(token);
                token = 0;
                Completion.SetCanceled();
            }
            catch (Exception error)
            {
                if (token != 0) NativeDol.DolDispose(token);
                token = 0;
                Completion.SetException(error);
            }
        }
    }
}
