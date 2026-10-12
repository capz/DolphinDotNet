using System;
using System.IO;
using Dolphin.Memory;
using System.Threading;
using System.Threading.Tasks;
using Scheduler = Dolphin.Threading.Tasks.Scheduler;

namespace Dolphin.IO;

/// <summary>Seekable, fixed-capacity stream backed by GameCube auxiliary RAM.
/// The stream owns its ARAM allocation; access is serialized and application-thread-only.</summary>
public sealed class AramStream : Stream
{
    private readonly AramBuffer buffer;
    private readonly bool writable;
    private int position;
    private int length;
    private bool disposed;
    private bool busy;

    /// <summary>Allocate a fixed capacity in ARAM, initially containing zero logical bytes.</summary>
    public AramStream(int capacity, bool writable = true)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException("capacity");
        buffer = Aram.Allocate(capacity);
        this.writable = writable;
    }

    public int Capacity => buffer.Length;
    public override bool CanRead => !disposed;
    public override bool CanWrite => !disposed && writable;
    public override bool CanSeek => !disposed;
    public override long Length { get { Check(); return length; } }
    public override long Position
    {
        get { Check(); return position; }
        set
        {
            CheckIdle();
            if (value < 0 || value > Capacity) throw new ArgumentOutOfRangeException("value");
            position = (int)value;
        }
    }

    private void Check()
    {
        if (disposed) throw new ObjectDisposedException("AramStream");
    }

    private void CheckIdle()
    {
        Check();
        if (busy) throw new InvalidOperationException("An ARAM stream transfer is in progress.");
    }

    public override int Read(byte[] destination, int offset, int count)
        => ReadAsync(destination, offset, count).GetAwaiter().GetResult();

    public override void Write(byte[] source, int offset, int count)
        => WriteAsync(source, offset, count).GetAwaiter().GetResult();

    // Dolphin's cooperative Task is not a Roslyn-recognized async return type.
    // Use a completion continuation until the public standard-Task migration is complete.
    public override Task<int> ReadAsync(byte[] destination, int offset, int count, CancellationToken token)
    {
        CheckIdle();
        Validate(destination, offset, count);
        if (token.IsCancellationRequested) return Task.FromCanceled<int>(token);
        int size = Math.Min(count, Math.Max(0, length - position));
        if (size == 0) return Task.FromResult(0);
        busy = true;
        var result = new TaskCompletionSource<int>();
        Task transfer;
        try { transfer = buffer.ReadAsync(position, destination, offset, size, token); }
        catch { busy = false; throw; }
        transfer.Continue(() =>
        {
            try
            {
                transfer.GetAwaiter().GetResult();
                position += size;
                result.SetResult(size);
            }
            catch (Exception error) { result.SetException(error); }
            finally { busy = false; }
        });
        return result.Task;
    }

    public override Task<int> ReadAsync(byte[] destination, int offset, int count)
        => ReadAsync(destination, offset, count, default);

    public override Task WriteAsync(byte[] source, int offset, int count, CancellationToken token)
    {
        CheckIdle();
        Validate(source, offset, count);
        if (!writable) throw new NotSupportedException("ARAM stream is read-only.");
        if (count > Capacity - position) throw new IOException("ARAM stream capacity exceeded.");
        if (position > length) throw new NotSupportedException("Sparse writes to ARAM are not supported.");
        if (token.IsCancellationRequested) return Task.FromCanceled(token);
        if (count == 0) return Task.CompletedTask;
        busy = true;
        var result = new TaskCompletionSource<int>();
        Task transfer;
        try { transfer = buffer.WriteAsync(source, offset, position, count, token); }
        catch { busy = false; throw; }
        transfer.Continue(() =>
        {
            try
            {
                transfer.GetAwaiter().GetResult();
                position += count;
                if (position > length) length = position;
                result.SetResult(0);
            }
            catch (Exception error) { result.SetException(error); }
            finally { busy = false; }
        });
        return result.Task;
    }

    public override Task WriteAsync(byte[] source, int offset, int count)
        => WriteAsync(source, offset, count, default);

    public override long Seek(long offset, SeekOrigin origin)
    {
        CheckIdle();
        long basePosition = origin == SeekOrigin.Begin ? 0 :
            origin == SeekOrigin.Current ? position :
            origin == SeekOrigin.End ? length : throw new ArgumentException("Invalid seek origin.");
        long next = basePosition + offset;
        if (next < 0 || next > Capacity) throw new IOException("Seek outside ARAM stream capacity.");
        position = (int)next;
        return next;
    }

    public override void SetLength(long value)
    {
        CheckIdle();
        if (!writable) throw new NotSupportedException();
        if (value < 0 || value > Capacity) throw new ArgumentOutOfRangeException("value");
        // Newly allocated ARAM is unspecified; growing exposes uninitialized data.
        if (value > length) throw new NotSupportedException("Grow by writing sequentially.");
        length = (int)value;
        if (position > length) position = length;
    }

    public override void Flush() { CheckIdle(); }

    public override void Dispose()
    {
        if (disposed) return;
        if (busy) throw new InvalidOperationException("Await the pending ARAM transfer before disposing.");
        disposed = true;
        buffer.Dispose();
    }
}
