using System;
using System.Net.Http;
using System.Threading.Tasks;
using Dolphin.Diagnostics;
using Dolphin.Graphics;
using Dolphin.Input;
using NetworkRuntime = Dolphin.Network.NetworkRuntime;

internal static class Program
{
    private const string ImageUrl = "http://dlptest.com/dlp-test-logo.png";

    private static async Task<int> Main()
    {
        try
        {
            DebugOverlay.WriteLine("HTTP TEXTURE: connecting BBA (DHCP / DNS)");
            await NetworkRuntime.StartAsync();
            DebugOverlay.WriteLine("Downloading http://dlptest.com/dlp-test-logo.png");
            using var client = new HttpClient();
            // The runtime's bounded default response buffer is 64 KiB; the PNG
            // currently occupies about 4 KiB. Non-success HTTP status is rejected.
            var png = await client.GetByteArrayAsync(ImageUrl);
            using var texture = Texture2D.FromPng(png, linear: true);
            DebugOverlay.WriteLine("PNG decoded to GX RGBA8. B: exit  Z: statistics");

            var device = GraphicsDevice.Default;
            using var draw = new Draw2D(device, new Draw2DOptions {
                Width = 640, Height = 480, VertexCapacity = 6
            });
            // Fit the complete image, preserving aspect ratio and reserving
            // the upper portion of the screen for download/error diagnostics.
            float scale = 576f / texture.Width;
            if (texture.Height * scale > 320) scale = 320f / texture.Height;
            float width = texture.Width * scale, height = texture.Height * scale;
            var destination = new RectF((640-width)/2, 100+(320-height)/2, width, height);
            while (true)
            {
                Controller.Update();
                if (Controller.GetState(0).B.WasPressed) return 0;
                device.BeginFrame(new Color32(240,240,240));
                draw.Begin();
                draw.DrawTexture(texture,destination,Color32.White);
                draw.End();
                device.EndFrame();
            }
        }
        catch (Exception error)
        {
            DebugOverlay.WriteLine("HTTP TEXTURE FAILED");
            DebugOverlay.WriteLine(error.Message);
            return 1;
        }
        finally
        {
            if (NetworkRuntime.IsReady) NetworkRuntime.Stop();
        }
    }
}
