using System;
using System.Net.Http;
using System.Threading.Tasks;
using Dolphin.IO;
using Dolphin.Memory;
using NetworkRuntime = Dolphin.Network.NetworkRuntime;
using Scheduler = Dolphin.Threading.Tasks.Scheduler;

internal static class Program
{
    // Set to the LAN IPv4 address of the computer running logo_proxy.py.
    // GameCube HTTP currently does not support HTTPS. The relay downloads
    // Google's official PNG over HTTPS and serves it locally over HTTP.
    private const string LogoUrl = "http://192.168.1.2:18081/google-logo.png";
    private const int MaximumLogoBytes = 256 * 1024;

    private static int Main()
    {
        var task = DownloadLogoToAramAsync();
        try
        {
            // Both queues must be serviced. On a real game, run these in
            // your regular input/update/render loop with a bounded budget.
            while (!task.IsCompleted)
            {
                NetworkRuntime.Pump(16, 1);
                Scheduler.Pump(16);
            }
            return task.GetAwaiter().GetResult();
        }
        catch (Exception error)
        {
            return 1;
        }
        finally
        {
            if (NetworkRuntime.IsReady) NetworkRuntime.Stop();
        }
    }

    private static async Task<int> DownloadLogoToAramAsync()
    {
        if (!Aram.Initialize())
            throw new InvalidOperationException("ARAM initialization failed.");

        await NetworkRuntime.StartAsync();

        using var client = new HttpClient();
        using var response = await client.GetAsync(
            LogoUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        // Keep a single reusable staging buffer in MEM1; never materialize
        // the entire HTTP response as byte[] in the managed heap.
        using var source = await response.Content.ReadAsStreamAsync();
        using var destination = new AramStream(MaximumLogoBytes);
        var scratch = new byte[4096];

        int received;
        while ((received = await source.ReadAsync(scratch, 0, scratch.Length)) != 0)
        {
            await destination.WriteAsync(scratch, 0, received);
        }

        var storedLength = destination.Length;
        destination.Position = 0;
        var signature = new byte[8];
        if (await destination.ReadAsync(signature, 0, signature.Length) != signature.Length ||
            signature[0] != 137 || signature[1] != 80 ||
            signature[2] != 78 || signature[3] != 71 ||
            signature[4] != 13 || signature[5] != 10 ||
            signature[6] != 26 || signature[7] != 10)
            throw new InvalidOperationException("Downloaded data is not a PNG.");

        if (storedLength == 0) throw new InvalidOperationException("Empty logo.");
        // Keep destination alive for as long as the game needs the asset.
        // Disposing here demonstrates cleanup; move it to a long-lived owner
        // in a real game so it remains available for later ReadAsync calls.
        return 0;
    }
}
