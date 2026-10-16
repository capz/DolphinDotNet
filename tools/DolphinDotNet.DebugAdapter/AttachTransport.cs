using System.Text.Json;

namespace DolphinDotNet.DebugAdapter;

/// <summary>Connection settings only: the expected EXI slot is build metadata, never a host command.</summary>
internal sealed record AttachTransport(string Kind, string? Host, int Port, string? SerialPort,
    int BaudRate, TimeSpan Timeout)
{
    public static AttachTransport Parse(JsonElement args)
    {
        var kind = args.TryGetProperty("transport", out var t) ? t.GetString() : "tcp";
        var seconds = args.TryGetProperty("timeoutSeconds", out var s) ? s.GetInt32() : 10;
        if (seconds is < 1 or > 120) throw new ArgumentException("timeoutSeconds must be between 1 and 120.");
        var timeout = TimeSpan.FromSeconds(seconds);
        if (kind == "tcp")
        {
            var host = Text(args, "host");
            var port = args.GetProperty("port").GetInt32();
            if (port is < 1 or > 65535) throw new ArgumentException("port must be between 1 and 65535.");
            return new(kind, host, port, null, 0, timeout);
        }
        if (kind == "usbGecko")
        {
            var serialPort = Text(args, "serialPort");
            // Required explicitly: this is a VCP driver compatibility value, not
            // an assertion about the FT245 FIFO transfer speed or tested hardware.
            var baudRate = args.GetProperty("baudRate").GetInt32();
            if (baudRate <= 0) throw new ArgumentException("baudRate must be positive.");
            return new(kind, null, 0, serialPort, baudRate, timeout);
        }
        throw new ArgumentException("transport must be tcp or usbGecko.");
    }

    private static string Text(JsonElement args, string name)
        => args.TryGetProperty(name, out var value) && value.GetString() is string text && !string.IsNullOrWhiteSpace(text)
            ? text.Trim() : throw new ArgumentException($"{name} is required.");
}
