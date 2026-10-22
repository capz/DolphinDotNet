using Dolphin.Audio;
using Dolphin.Graphics;

internal static class Program
{
    private static int Main()
    {
        using var sound = Sound.FromPcm8(new byte[320], 16000);
        using var channel = sound.Play();
        var device = GraphicsDevice.Default;
        device.BeginFrame(new Color32(0, 0, 0));
        device.EndFrame();
        return 0;
    }
}
