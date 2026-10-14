using System;
using System.Net.Http;
using System.Threading.Tasks;
using Dolphin.Memory;
using AramStream = Dolphin.IO.AramStream;
using NetworkRuntime = Dolphin.Network.NetworkRuntime;
using Scheduler = Dolphin.Threading.Tasks.Scheduler;

internal static class Program
{
    // Set to the LAN IPv4 address of the computer running logo_proxy.py.
    // GameCube HTTP currently does not support HTTPS. The relay downloads
    // Google's official PNG over HTTPS and serves it locally over HTTP.
    private const string LogoUrl = "http://192.168.1.2:18081/google-logo.png";
    private const int MaximumLogoBytes = 256 * 1024;
    private const int CopyBufferBytes = 4096;

    private static int Main()
    {
        // Main is the application entry point. Starting an async method gives
        // us a Task representing the download; it does not block on network I/O.
        try
        {
            var downloadTask = DownloadLogoToAramAsync();

            // GameCube/DolphinDotNet uses cooperative I/O instead of a desktop
            // background thread pool. Pending operations advance only when the
            // application services both pumps:
            //
            // NetworkRuntime.Pump: advances DHCP, DNS, sockets and HTTP requests.
            // Scheduler.Pump: runs async continuations and polls ARAM DMA work.
            //
            // The limits bound work per iteration; they are not a guaranteed
            // frame-time budget. A game places these calls in its normal loop,
            // alongside input, update and rendering, rather than spinning here.
            while (!downloadTask.IsCompleted)
            {
                NetworkRuntime.Pump(16, 1);
                Scheduler.Pump(16);
            }

            // The Task is complete: observe any error and use its exit code.
            return downloadTask.GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            return 1; // Download, ARAM, or PNG verification failed.
        }
        finally
        {
            // Release network resources even when the download fails.
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

        // The standard Stream.CopyToAsync API reuses a 4 KiB MEM1 buffer and
        // awaits each read and ARAM write. AramStream implements System.IO.Stream;
        // its public async methods use System tasks and cancellation tokens.
        // The entire image stays in auxiliary RAM rather than a MEM1 byte array.
        using var httpResponseStream = await response.Content.ReadAsStreamAsync();
        using var aramStream = new AramStream(MaximumLogoBytes);
        await httpResponseStream.CopyToAsync(aramStream, CopyBufferBytes);

        if (aramStream.Length == 0) throw new InvalidOperationException("Empty logo.");
        aramStream.Position = 0;
        var signature = new byte[8];
        // Stream reads may return fewer bytes than requested; fill the header
        // using the supported byte-array overload instead of ReadExactlyAsync.
        var signatureBytes = 0;
        while (signatureBytes < signature.Length)
        {
            var received = await aramStream.ReadAsync(
                signature, signatureBytes, signature.Length - signatureBytes);
            if (received == 0) throw new InvalidOperationException("Incomplete PNG signature.");
            signatureBytes += received;
        }
        if (signature[0] != 137 || signature[1] != 80 ||
            signature[2] != 78 || signature[3] != 71 ||
            signature[4] != 13 || signature[5] != 10 ||
            signature[6] != 26 || signature[7] != 10)
            throw new InvalidOperationException("Downloaded data is not a PNG.");

        // Later, seek and read only the bytes the game needs from aramStream
        // into a small main-RAM buffer (for decoding, textures, or other work).
        // Keep aramStream alive in an asset owner until then. This demo disposes
        // it on return, releasing the ARAM allocation.
        return 0;
    }
}
