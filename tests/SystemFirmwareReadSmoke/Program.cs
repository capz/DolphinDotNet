using Dolphin.System;
using Dolphin.Storage;
using Dolphin.Graphics;
using Dolphin.Audio;
using Dolphin.Diagnostics;
using System;
using System.Threading;
internal static class Program
{
    private static int Main()
    {
        using(var image=SystemFirmware.ReadConsole())
        using(var catalog=image.CreateCatalog())
        {
            if(image.Source!=FirmwareSource.ConsoleRom||catalog.Count!=4||catalog.Files!=null||catalog.GetStartupAudio().Layout!=FirmwareAudioLayout.AdjacentWaveArchive||
               catalog.GetTexture(SystemTexture.Environment).HeaderOffset!=8448||catalog.GetStartupAudio().SampleRate!=32000||catalog.WaveSystemCount!=1)return 1;
            var device=GraphicsDevice.Default;int before=device.NativeBytes;
            using(var texture=SystemResources.CreateTexture(catalog,SystemTexture.Environment))
                if(texture.Width!=8||texture.Height!=8||texture.MipLevels!=1||texture.NativeBytes<=0)return 5;
            if(device.NativeBytes!=before)return 6;
            using(var model=SystemResources.CreateModelAsync(catalog,SystemModel.BaseCube).Result)
            {
                if(model.NodeCount!=1||model.MeshCount!=1||model.MaterialCount!=1||model.GetNodeName(0)!="boot_demo_base_cube")return 15;
                using(var instance=model.CreateInstance())
                {var transform=new float[16];instance.GetNodeTransform(0,transform);if(transform[15]!=1)return 16;}
            }
        }
        if(!Storage.Mount(StorageDevice.SdSerialPort2))return 2;
        using(var image=SystemFirmware.Open("sd:/firmware-synthetic.bin"))
        using(var catalog=image.CreateCatalog())
        {
            if(image.Source!=FirmwareSource.ExplicitFile||catalog.Count!=4||catalog.Extract(0)[0]!=65)return 3;
            DebugOverlay.WriteLine("FIRMWARE AUDIO BEGIN");
            using(var sound=SystemResources.CreateStartupSound(catalog))
            {
                DebugOverlay.WriteLine("FIRMWARE AUDIO VALIDATED");
                if(sound.Channels!=2||sound.SampleRate!=32000||sound.SampleCount!=174067)return 7;
                DebugOverlay.WriteLine("FIRMWARE AUDIO PLAY");
                var channel=sound.Play();if(!channel.IsPlaying)return 8;channel.Stop();
                var second=sound.PlayLoop(3,20);if(!second.IsPlaying)return 9;
                using(var music=SystemResources.CreateStartupMusic(catalog))
                {
                    image.Dispose();catalog.Dispose();
                    music.Play();if(!music.IsPlaying)return 10;music.Stop();
                }
                if(!second.IsPlaying)return 13;
            }
            DebugOverlay.WriteLine("FIRMWARE AUDIO DISPOSED");
            try{Sound.FromEncoded(new byte[32],AudioFormat.Afc);return 11;}catch(global::System.IO.InvalidDataException){}
            using(var canceled=new CancellationTokenSource())
            {
                canceled.Cancel();
                try{SystemResources.CreateStartupSound(catalog,canceled.Token);return 12;}catch(OperationCanceledException){}
            }
        }
        using(var fallback=SystemFirmware.Open("sd:/firmware-synthetic.bin"))
            if(fallback.Source!=FirmwareSource.ExplicitFile)return 14;
        try{throw new global::System.IO.EndOfStreamException("synthetic short read");}
        catch(global::System.IO.IOException){}
        using(var cancellation=new CancellationTokenSource())
        {
            cancellation.Cancel();
            try{SystemFirmware.ReadConsole(cancellation.Token);return 4;}
            catch(OperationCanceledException){}
        }
        return 0;
    }
}
