using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Dolphin.Diagnostics;
using Dolphin.Serialization.Json;
using NetworkRuntime=Dolphin.Network.NetworkRuntime;
using NetworkOptions=Dolphin.Network.NetworkOptions;
using PlatformIPAddress=Dolphin.Network.IPAddress;

internal static class Program
{
    private const string ConfigUrl="http://config.test:18081/json";
    private static async Task<int> Main()
    {
        var watch=Dolphin.Timing.Stopwatch.StartNew();string stage="CONNECT";
        using var cancel=new CancellationTokenSource();
        using var timeout=Dolphin.Timing.Timer.Schedule(()=>cancel.Cancel(),15000);
        try
        {
            await NetworkRuntime.StartAsync(new NetworkOptions {DnsServers=new[]{PlatformIPAddress.Loopback},DnsPort=15353},cancel.Token);
            stage="HTTP";using var http=new HttpClient();
            using var response=await http.GetAsync(ConfigUrl,cancel.Token);
            DebugOverlay.WriteLine($"HTTP {(int)response.StatusCode:D3}");response.EnsureSuccessStatusCode();
            var json=await response.Content.ReadAsStringAsync();stage="JSON";
            int value=await Task.Run(()=>ReadValue(json));
            DebugOverlay.WriteLine($"VALUE {value:D2} IN {watch.ElapsedSeconds:F3} SECONDS");
            return value==42?0:1;
        }
        catch(Exception error){DebugOverlay.WriteLine($"{stage} FAILED: {error.Message}");return 1;}
        finally{if(NetworkRuntime.IsReady)NetworkRuntime.Stop();}
    }
    private static int ReadValue(string json)
    {
        var reader=new JsonReader(json);
        while(reader.Read())if(reader.TokenType==JsonTokenType.PropertyName&&reader.Value=="value")
        {if(!reader.Read())throw new FormatException("Missing value");return reader.GetInt32();}
        throw new FormatException("Missing value property");
    }
}
