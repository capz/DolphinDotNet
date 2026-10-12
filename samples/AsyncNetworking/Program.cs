using System;
using System.Net.Http;
using System.Threading.Tasks;
using Dolphin.Serialization.Json;
using NetworkRuntime = Dolphin.Network.NetworkRuntime;
using NetworkOptions = Dolphin.Network.NetworkOptions;
using Scheduler = Dolphin.Threading.Tasks.Scheduler;
using NativeIPAddress = Dolphin.Network.IPAddress;

internal static class Program
{
    // These defaults match tests/network_fixtures.py. Configure your own resolver,
    // DNS port (normally 53), and reachable HTTP endpoint for a console build.
    private static int Main()
    {
        var download = DownloadValueAsync();
        while (!download.IsCompleted)
        {
            NetworkRuntime.Pump();
            Scheduler.Pump(16);
            // A game performs input, update and rendering here each frame.
        }

        // GetResult is safe here: the task has already completed, including faults.
        try { return download.GetAwaiter().GetResult() == 42 ? 0 : 1; }
        finally { if (NetworkRuntime.IsReady) NetworkRuntime.Stop(); }
    }

    private static async Task<int> DownloadValueAsync()
    {
        // Direct Dolphin APIs take Dolphin address/token types. Await bridges to
        // this ordinary System.Threading.Tasks.Task method during AOT compilation.
        await NetworkRuntime.StartAsync(new NetworkOptions {
            DnsServers = new[] { NativeIPAddress.Loopback },
            DnsPort = 15353
        }, default);

        using var http = new HttpClient();
        var json = await http.GetStringAsync("http://config.test:18081/json");
        var reader = new JsonReader(json);
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName && reader.Value == "value")
            {
                if (!reader.Read()) throw new FormatException("Missing value.");
                return reader.GetInt32();
            }
        }
        throw new FormatException("Missing value property.");
    }
}
