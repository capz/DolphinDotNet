using Dolphin.Audio;
using Dolphin.Diagnostics;
using Dolphin.Storage;

internal static class Program
{
    private static async Task<int> Main()
    {
        if(!Storage.Mount(StorageDevice.SdSerialPort2))return 1;
        Audio.Initialize();
        using var sfx=await Sound.LoadAsync("sd:/audio/laser.wav");
        using var music=await Music.LoadAsync("sd:/audio/theme.wav");
        music.Volume=0.4f;
        music.Play(loop:true);
        using(var voice=sfx.Play())
        {
            voice.Volume=0.8f;
            voice.Pitch=1.2f;
            await Task.Delay(1000);
        }
        music.Stop();
        DebugOverlay.WriteLine("Audio playback finished");
        return 0;
    }
}
