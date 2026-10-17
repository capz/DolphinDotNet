using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace DolphinDotNet.DebugAdapter;

/// <summary>Independent UDP replies; never issues commands on the GDB stream.</summary>
internal sealed class DebugHeartbeat : IAsyncDisposable
{
    public const uint Magic = 0x444e4842;
    private readonly UdpClient _socket;
    private readonly CancellationTokenSource _cancel = new();
    private readonly byte[] _token = RandomNumberGenerator.GetBytes(8);
    private readonly Task _worker;
    public byte[] Configuration { get; }

    public static (IPAddress Host, int Port)? Parse(JsonElement args)
    {
        if (!args.TryGetProperty("heartbeatHost", out var value))
        {
            if (args.TryGetProperty("heartbeatPort", out _)) throw new ArgumentException("heartbeatPort requires heartbeatHost.");
            return null;
        }
        var text = value.GetString()?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        var parts = text.Split('.');
        if (parts.Length != 4 || parts.Any(p => p.Length is < 1 or > 3 || p.Any(c => c < '0' || c > '9') || !byte.TryParse(p, out _)))
            throw new ArgumentException("heartbeatHost must be a unicast IPv4 address reachable from the game.");
        var host = new IPAddress(parts.Select(byte.Parse).ToArray());
        if (host.GetAddressBytes()[0] is 0 or >= 224)
            throw new ArgumentException("heartbeatHost must be a unicast IPv4 address reachable from the game.");
        var port = args.TryGetProperty("heartbeatPort", out var p) ? p.GetInt32() : 2167;
        if (port is < 0 or > 65535) throw new ArgumentException("heartbeatPort must be 0 (automatic) or between 1 and 65535.");
        return (host, port);
    }

    public DebugHeartbeat(IPAddress host, int port, Func<bool> connected)
    {
        while (_token.All(value => value == 0)) RandomNumberGenerator.Fill(_token);
        _socket = new UdpClient(new IPEndPoint(IPAddress.Any, port));
        // Bounded socket queues; packets do not grow any application collection.
        _socket.Client.ReceiveBufferSize = 4096;
        Configuration = new byte[20];
        host.GetAddressBytes().CopyTo(Configuration, 0);
        BinaryPrimitives.WriteUInt32BigEndian(Configuration.AsSpan(4), (uint)((IPEndPoint)_socket.Client.LocalEndPoint!).Port);
        _token.CopyTo(Configuration, 8);
        BinaryPrimitives.WriteUInt32BigEndian(Configuration.AsSpan(16), 1);
        _worker = RunAsync(connected);
    }

    private async Task RunAsync(Func<bool> connected)
    {
        try
        {
            while (!_cancel.IsCancellationRequested)
            {
                var packet = await _socket.ReceiveAsync(_cancel.Token);
                var bytes = packet.Buffer;
                if (bytes.Length != 20 || BinaryPrimitives.ReadUInt32BigEndian(bytes) != Magic ||
                    BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4)) != 1 ||
                    !CryptographicOperations.FixedTimeEquals(bytes.AsSpan(8, 8), _token) ||
                    BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16)) == 0 || !connected()) continue;
                BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), 2);
                await _socket.SendAsync(bytes, packet.RemoteEndPoint, _cancel.Token);
            }
        }
        catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException or SocketException) { }
    }

    public async ValueTask DisposeAsync()
    {
        _cancel.Cancel();
        _socket.Dispose();
        await _worker;
        _cancel.Dispose();
    }
}
