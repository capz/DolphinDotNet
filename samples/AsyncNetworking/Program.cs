using System;
using System.Net.Http;
using System.Threading.Tasks;
using Dolphin.Serialization.Json;
using NetworkRuntime = Dolphin.Network.NetworkRuntime;
using NetworkOptions = Dolphin.Network.NetworkOptions;
using PlatformIPAddress = Dolphin.Network.IPAddress;

internal static class Program
{
    private const string ConfigUrl = "http://config.test:18081/json";
    private const int DnsPort = 15353;

    // These defaults match tests/network_fixtures.py. Configure your own resolver,
    // DNS port (normally 53), and reachable HTTP endpoint for a console build.
    private static async Task<int> Main()
    {
        try { return await DownloadValueAsync() == 42 ? 0 : 1; }
        catch (Exception) { return 1; }
        finally { if (NetworkRuntime.IsReady) NetworkRuntime.Stop(); }
    }

    private static async Task<int> DownloadValueAsync()
    {
        // Platform startup uses Dolphin IPv4 addresses, but standard tasks and
        // cancellation tokens. Networking advances on the runtime service thread.
        await NetworkRuntime.StartAsync(new NetworkOptions {
            DnsServers = new[] { PlatformIPAddress.Loopback },
            DnsPort = DnsPort
        }, default);

        using var http = new HttpClient();
        var json = await http.GetStringAsync(ConfigUrl);
        // Run bounded parsing on the background executor. This method has no
        // graphics/input work, so its continuation need not return to the app.
        return await Task.Run(() => ReadValue(json)).ConfigureAwait(false);
    }

    private static int ReadValue(string json)
    {
        // The reflection-free Dolphin reader is supported; System.Text.Json is
        // not yet available in the AOT runtime.
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
