using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Dolphin.Network;

/// <summary>A certificate-validated TLS connection with caller-owned nonblocking I/O buffers.</summary>
/// <remarks>Uses the configured trust, entropy and clock and negotiates HTTP/1.1.
/// Retry a would-block write with the same unmodified buffer range. This class
/// does not add HTTP, WebSocket framing or application authentication.</remarks>
public sealed class TlsStream : IDisposable
{
    private readonly TcpClient client;
    private readonly int[] endpoint = new int[2];
    private bool disposed;

    private TlsStream(TcpClient client) { this.client = client; }

    public static Task<TlsStream> ConnectAsync(string hostname, int port, CancellationToken cancellationToken)
    {
        NetworkRuntime.RequireReady();
        if (hostname == null) throw new ArgumentNullException("hostname");
        if (hostname.Length == 0 || hostname.Length > 253 || port < 1 || port > 65535)
            throw new ArgumentException("Invalid TLS endpoint.");
        cancellationToken.ThrowIfCancellationRequested();
        var client = new TcpClient();
        try
        {
            var work = new ConnectWork(client, hostname, port, cancellationToken);
            NetworkRuntime.Add(work);
            return work.Completion.Task;
        }
        catch { client.Dispose(); throw; }
    }

    // -1 means would block, zero means EOF. No Task or result allocation per poll.
    public int TryRead(byte[] buffer, int offset, int count)
    {
        Check(); Socket.Validate(buffer, offset, count);
        if (count == 0) return 0;
        int read = NativeNetwork.NetRead(client.Client.Handle, buffer, offset, count, endpoint);
        NetworkRuntime.Check(read); return read;
    }

    // A positive result can be a short write. -1 leaves the buffer owned by the caller.
    public int TryWrite(byte[] buffer, int offset, int count)
    {
        Check(); Socket.Validate(buffer, offset, count);
        if (count == 0) return 0;
        int written = NativeNetwork.NetWrite(client.Client.Handle, buffer, offset, count, 0, 0);
        NetworkRuntime.Check(written); return written;
    }

    /// <summary>Fill up to 256 bytes from the configured cryptographic DRBG,
    /// for protocol nonces and client WebSocket masks. Pool small draws to stay
    /// inside the provider's bounded entropy interval. Exhaustion fails closed
    /// and requires fresh configuration after all TLS streams are closed.</summary>
    public void FillRandom(byte[] buffer, int offset, int count)
    {
        Check(); Socket.Validate(buffer, offset, count);
        if (count > 256) throw new ArgumentOutOfRangeException("count");
        if (NativeNetwork.TlsRandom(client.Client.Handle, buffer, offset, count) < 0)
            throw new IOException("TLS random state requires fresh entropy.");
    }

    private void Check()
    {
        if (disposed) throw new ObjectDisposedException("TlsStream");
        client.Client.Check();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; client.Dispose();
    }

    private sealed class ConnectWork : Operation
    {
        private readonly TcpClient client;
        private readonly string hostname;
        private readonly int port;
        private readonly CancellationToken cancellation;
        private Task? connecting;
        private bool startedTls;
        internal readonly TaskCompletionSource<TlsStream> Completion = new TaskCompletionSource<TlsStream>();
        internal ConnectWork(TcpClient client, string hostname, int port, CancellationToken cancellation) : base(cancellation)
        { this.client = client; this.hostname = hostname; this.port = port; this.cancellation = cancellation; }

        protected override bool Step()
        {
            if (connecting == null) { connecting = client.ConnectAsync(hostname, port, cancellation); return false; }
            if (!connecting.IsCompleted) return false;
            connecting.GetAwaiter().GetResult();
            if (!startedTls)
            {
                if (NativeNetwork.TlsStart(client.Client.Handle, hostname) < 0)
                    throw new IOException("TLS is unavailable or not securely configured.");
                startedTls = true;
            }
            int result = NativeNetwork.TlsHandshake(client.Client.Handle);
            if (result == -1) return false;
            if (result < 0) throw new IOException("TLS handshake or certificate verification failed.");
            Completion.SetResult(new TlsStream(client)); return true;
        }
        protected override void Fail(Exception error) { client.Dispose(); Completion.SetException(error); }
    }
}
