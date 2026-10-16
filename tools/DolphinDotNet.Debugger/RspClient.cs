using System.Globalization;
using System.IO.Ports;
using System.Net.Sockets;
using System.Text;

namespace DolphinDotNet.Debugger;

/// <summary>
/// GDB Remote Serial Protocol (RSP) client, independent of GameCube transport.
/// The client uses RSP acknowledgement mode; it does not assume qSupported,
/// vCont, no-ack mode, thread enumeration or any stub-specific feature exists.
/// </summary>
public sealed class RspClient : IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly IDisposable? _owner;
    private readonly bool _leaveOpen;
    private readonly SemaphoreSlim _commands = new(1, 1);
    private readonly SemaphoreSlim _writes = new(1, 1);
    private readonly byte[] _byte = new byte[1];
    private readonly TimeSpan _timeout;
    private bool _disposed;

    public RspClient(Stream stream, bool leaveOpen = true, TimeSpan? timeout = null)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        if (!stream.CanRead || !stream.CanWrite)
            throw new ArgumentException("RSP requires a readable and writable stream.", nameof(stream));
        _leaveOpen = leaveOpen;
        _timeout = timeout ?? TimeSpan.FromSeconds(10);
        if (_timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    private RspClient(Stream stream, IDisposable owner, TimeSpan? timeout)
        : this(stream, leaveOpen: false, timeout: timeout)
        => _owner = owner;

    /// <summary>Connect to Dolphin Emulator's GDB TCP port or a real GameCube BBA debug stub.</summary>
    public static async Task<RspClient> ConnectTcpAsync(string host, int port,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("Host is required.", nameof(host));
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        var tcp = new TcpClient();
        try
        {
            await tcp.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
            tcp.NoDelay = true;
            return new RspClient(tcp.GetStream(), tcp, timeout);
        }
        catch
        {
            tcp.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Open the FTDI virtual serial port of a USB Gecko. The baud rate must be
    /// explicitly supplied and validated on the user's adapter/driver hardware.
    /// No baud or COM-device autodetection is attempted.
    /// </summary>
    public static RspClient ConnectUsbGecko(string portName, int baudRate, TimeSpan? timeout = null)
    {
        if (string.IsNullOrWhiteSpace(portName))
            throw new ArgumentException("Serial port path is required.", nameof(portName));
        if (baudRate <= 0) throw new ArgumentOutOfRangeException(nameof(baudRate));
        var port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            ReadTimeout = (int)Math.Min(int.MaxValue, (timeout ?? TimeSpan.FromSeconds(10)).TotalMilliseconds),
            WriteTimeout = (int)Math.Min(int.MaxValue, (timeout ?? TimeSpan.FromSeconds(10)).TotalMilliseconds)
        };
        try
        {
            port.Open();
            return new RspClient(port.BaseStream, port, timeout);
        }
        catch
        {
            port.Dispose();
            throw;
        }
    }

    public static string[] SerialPortNames() => SerialPort.GetPortNames();

    /// <summary>Perform one RSP request/response with bounded time and serialized access.</summary>
    public async Task<string> CommandAsync(string command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(command) || command.Any(c => c is '$' or '#' or '}' or '*' || c > 0x7f))
            throw new ArgumentException("Invalid literal RSP command.", nameof(command));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timeout);
        await _commands.WaitAsync(deadline.Token).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var frame = Encode(command);
            await WriteAsync(frame, deadline.Token).ConfigureAwait(false);
            return await ReadReplyAsync(frame, deadline.Token).ConfigureAwait(false);
        }
        finally
        {
            _commands.Release();
        }
    }

    /// <summary>Ask about remote features. An empty result is legal for older libdb stubs.</summary>
    public async Task<IReadOnlyDictionary<string, string?>> FeaturesAsync(CancellationToken cancellationToken = default)
    {
        var reply = await CommandAsync("qSupported", cancellationToken).ConfigureAwait(false);
        if (reply.StartsWith('E')) throw new IOException("Remote feature probe rejected: " + reply);
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var feature in reply.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var delimiter = feature.IndexOf('=');
            if (delimiter >= 0) result[feature[..delimiter]] = feature[(delimiter + 1)..];
            else if (feature.EndsWith('+') || feature.EndsWith('-'))
                result[feature[..^1]] = feature[^1..];
            else result[feature] = null;
        }
        return result;
    }

    public Task<RspStop> QueryStopAsync(CancellationToken token = default) => StopCommandAsync("?", token);
    public Task<RspStop> ContinueAsync(CancellationToken token = default) => StopCommandAsync("c", token);
    public Task<RspStop> StepInstructionAsync(CancellationToken token = default) => StopCommandAsync("s", token);

    private async Task<RspStop> StopCommandAsync(string command, CancellationToken token)
        => RspStop.Parse(await CommandAsync(command, token).ConfigureAwait(false));

    /// <summary>Request an asynchronous interrupt while ContinueAsync is awaiting the stop reply.</summary>
    public async Task InterruptAsync(CancellationToken token = default)
    {
        ThrowIfDisposed();
        await WriteAsync(new byte[] { 3 }, token).ConfigureAwait(false);
    }

    public async Task SetSoftwareBreakpointAsync(uint address, CancellationToken token = default)
        => await ChangeBreakpointAsync("Z0", address, token).ConfigureAwait(false);
    public async Task ClearSoftwareBreakpointAsync(uint address, CancellationToken token = default)
        => await ChangeBreakpointAsync("z0", address, token).ConfigureAwait(false);

    private async Task ChangeBreakpointAsync(string opcode, uint address, CancellationToken token)
    {
        if (address == 0 || (address & 3) != 0)
            throw new ArgumentOutOfRangeException(nameof(address), "PowerPC breakpoints are 4-byte aligned.");
        var response = await CommandAsync($"{opcode},{address:x8},4", token).ConfigureAwait(false);
        if (response != "OK")
            throw new IOException($"GDB {opcode} breakpoint at 0x{address:x8} failed: {response}");
    }

    /// <summary>Read a bounded block. libogc2 has only a 2 KiB packet buffer.</summary>
    public async Task<byte[]> ReadMemoryAsync(uint address, int size, CancellationToken token = default)
    {
        if (size is < 1 or > 512) throw new ArgumentOutOfRangeException(nameof(size));
        if ((ulong)address + (uint)size > uint.MaxValue + 1UL)
            throw new ArgumentOutOfRangeException(nameof(address));
        var response = await CommandAsync($"m{address:x8},{size:x}", token).ConfigureAwait(false);
        if (response.Length != size * 2)
            throw new InvalidDataException("Incorrect GDB memory response length: " + response);
        try { return Convert.FromHexString(response); }
        catch (FormatException ex) { throw new InvalidDataException("Invalid GDB memory bytes.", ex); }
    }

    /// <summary>Register bytes remain raw until the target's architecture/byte order is known.</summary>
    public async Task<byte[]> ReadRegisterAsync(int index, CancellationToken token = default)
    {
        if (index is < 0 or > 255) throw new ArgumentOutOfRangeException(nameof(index));
        var response = await CommandAsync($"p{index:x}", token).ConfigureAwait(false);
        if (response.Length == 0 || (response.Length & 1) != 0)
            throw new InvalidDataException("Invalid register response: " + response);
        try { return Convert.FromHexString(response); }
        catch (FormatException ex) { throw new InvalidDataException("Invalid register bytes.", ex); }
    }

    private async Task<string> ReadReplyAsync(byte[] lastCommand, CancellationToken token)
    {
        var rejected = 0;
        var invalid = 0;
        while (true)
        {
            var first = await ReadByteAsync(token).ConfigureAwait(false);
            if (first == '+') continue;
            if (first == '-')
            {
                if (++rejected > 3) throw new IOException("GDB repeatedly rejected the command checksum.");
                await WriteAsync(lastCommand, token).ConfigureAwait(false);
                continue;
            }
            if (first != '$')
                throw new InvalidDataException($"Unexpected GDB response framing byte: 0x{first:x2}");

            var packet = new List<byte>(256);
            var sum = 0;
            while (true)
            {
                int value = await ReadByteAsync(token).ConfigureAwait(false);
                if (value == '#') break;
                if (packet.Count >= 16384) throw new InvalidDataException("Oversized GDB response.");
                packet.Add((byte)value);
                sum = (sum + value) & 255;
            }
            var hi = await ReadByteAsync(token).ConfigureAwait(false);
            var lo = await ReadByteAsync(token).ConfigureAwait(false);
            if (Hex(hi) < 0 || Hex(lo) < 0 || ((Hex(hi) << 4) | Hex(lo)) != sum)
            {
                await WriteAsync(new byte[] { (byte)'-' }, token).ConfigureAwait(false);
                if (++invalid > 3) throw new InvalidDataException("GDB response checksum failed repeatedly.");
                continue;
            }
            await WriteAsync(new byte[] { (byte)'+' }, token).ConfigureAwait(false);
            return Encoding.ASCII.GetString(Unescape(packet));
        }
    }

    private static byte[] Unescape(List<byte> input)
    {
        var output = new List<byte>(input.Count);
        for (int i = 0; i < input.Count; i++)
        {
            int next = input[i];
            if (next == '}')
            {
                if (++i == input.Count) throw new InvalidDataException("Incomplete GDB escaping.");
                next = input[i] ^ 0x20;
            }
            else if (next == '*')
            {
                if (output.Count == 0 || ++i == input.Count)
                    throw new InvalidDataException("Invalid GDB run-length encoding.");
                var repeat = input[i] - 29;
                if (repeat is < 0 or > 97)
                    throw new InvalidDataException("Invalid GDB run-length count.");
                if (output.Count + repeat > 16384) throw new InvalidDataException("Expanded GDB packet too large.");
                for (int j = 0; j < repeat; j++) output.Add(output[^1]);
                continue;
            }
            output.Add((byte)next);
        }
        return output.ToArray();
    }

    private static int Hex(int value) => value switch
    {
        >= '0' and <= '9' => value - '0',
        >= 'a' and <= 'f' => value - 'a' + 10,
        >= 'A' and <= 'F' => value - 'A' + 10,
        _ => -1
    };

    private static byte[] Encode(string command)
    {
        var payload = Encoding.ASCII.GetBytes(command);
        if (payload.Length > 1024) throw new ArgumentException("RSP command too long.", nameof(command));
        var frame = new byte[payload.Length + 4];
        frame[0] = (byte)'$';
        payload.CopyTo(frame, 1);
        frame[^3] = (byte)'#';
        byte checksum = (byte)(payload.Sum(b => (int)b) & 255);
        frame[^2] = (byte)"0123456789abcdef"[checksum >> 4];
        frame[^1] = (byte)"0123456789abcdef"[checksum & 15];
        return frame;
    }

    private async Task<int> ReadByteAsync(CancellationToken token)
    {
        var size = await _stream.ReadAsync(_byte.AsMemory(0, 1), token).ConfigureAwait(false);
        if (size == 0) throw new EndOfStreamException("Remote GDB connection was closed.");
        return _byte[0];
    }

    private async Task WriteAsync(byte[] bytes, CancellationToken token)
    {
        await _writes.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await _stream.WriteAsync(bytes, token).ConfigureAwait(false);
            await _stream.FlushAsync(token).ConfigureAwait(false);
        }
        finally { _writes.Release(); }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RspClient));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        // Do not dispose locks while an asynchronous command is in progress.
        // Closing the underlying connection unblocks pending reads.
        if (!_leaveOpen) await _stream.DisposeAsync().ConfigureAwait(false);
        _owner?.Dispose();
    }
}
