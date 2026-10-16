using System.Text;
using System.Text.Json;

namespace DolphinDotNet.DebugAdapter;

/// <summary>One DAP stream pair. No diagnostic text may be written to output.</summary>
public sealed class DapConnection(Stream input, Stream output)
{
    private readonly SemaphoreSlim _writes = new(1, 1);
    private int _sequence;
    public async Task<JsonDocument?> ReadAsync(CancellationToken token = default)
    {
        var header = new List<byte>();
        var one = new byte[1];
        while (true)
        {
            if (await input.ReadAsync(one, token) == 0)
            {
                if (header.Count == 0) return null;
                throw new EndOfStreamException("Truncated DAP header.");
            }
            header.Add(one[0]);
            if (header.Count > 8192) throw new InvalidDataException("DAP header too large.");
            if (header.Count >= 4 && header[^4] == 13 && header[^3] == 10 && header[^2] == 13 && header[^1] == 10) break;
        }
        int? length = null;
        foreach (var line in Encoding.ASCII.GetString(header.ToArray()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = line.IndexOf(':');
            if (colon < 1) throw new InvalidDataException("Invalid DAP header.");
            if (!line[..colon].Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
            if (length.HasValue || !int.TryParse(line[(colon + 1)..].Trim(), out var size) || size is < 1 or > 1048576)
                throw new InvalidDataException("Invalid or duplicate DAP Content-Length.");
            length = size;
        }
        if (!length.HasValue) throw new InvalidDataException("Missing DAP Content-Length.");
        var body = new byte[length.Value];
        await input.ReadExactlyAsync(body, token);
        return JsonDocument.Parse(body);
    }

    public Task RespondAsync(JsonElement request, object? body = null, string? error = null)
        => SendAsync(new Dictionary<string, object?>
        {
            ["type"] = "response", ["request_seq"] = request.GetProperty("seq").GetInt32(),
            ["command"] = request.GetProperty("command").GetString(), ["success"] = error is null,
            ["body"] = body, ["message"] = error
        });

    public Task EventAsync(string name, object? body = null)
        => SendAsync(new Dictionary<string, object?> { ["type"] = "event", ["event"] = name, ["body"] = body });

    private async Task SendAsync(Dictionary<string, object?> message)
    {
        await _writes.WaitAsync();
        try
        {
            message["seq"] = ++_sequence;
            var payload = JsonSerializer.SerializeToUtf8Bytes(message);
            await output.WriteAsync(Encoding.ASCII.GetBytes($"Content-Length: {payload.Length}\r\n\r\n"));
            await output.WriteAsync(payload);
            await output.FlushAsync();
        }
        finally { _writes.Release(); }
    }
}
