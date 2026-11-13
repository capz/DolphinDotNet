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
        if(FirmwareAudioFingerprint.Hash(new byte[]{97,98,99},0,3)!="ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")return 30;
        if(FirmwareAudioFingerprint.Hash(new byte[64],0,64)!="f5a5fd42d16a20302798ef6ed309979b43003d2320d9f0e8ea9831a92759fb4b")return 33;
        var sequence=new SystemStartupSequence();sequence.Seek(4.2);if(sequence.Stage!=SystemStartupStage.Letters)return 19;
        using(var image=SystemFirmware.ReadConsole())
        using(var catalog=image.CreateCatalog())
        {
            if(image.Source!=FirmwareSource.ConsoleRom||catalog.Count!=6||catalog.Files!=null||catalog.GetStartupAudio().Layout!=FirmwareAudioLayout.AdjacentWaveArchive||
               catalog.GetTexture(SystemTexture.Environment).HeaderOffset!=8448||catalog.GetStartupAudio().SampleRate!=32000||catalog.WaveSystemCount!=1)return 1;
            if(catalog[4].Offset!=0x1aff00||catalog[5].Offset!=0x1fcf00||catalog.Extract(4)[0]!=70)return 21;
            if(catalog.GetWaveSystem(0)[2].WaveId!=102||catalog.GetWaveSystem(0)[2].BaseKey!=0)return 34;
            using(var menu=image.CreateCatalog(FirmwareCatalogOptions.IncludeMenuAudio,default))
            {
                SystemMenuSoundInfo info;
                if(menu.TryGetMenuSoundInfo(SystemMenuSound.TurnSide,out info)||info!=null||menu.MenuAudioRetainedBytes!=0)return 22;
                try{SystemResources.CreateMenuSound(menu,SystemMenuSound.Select);return 23;}catch(NotSupportedException){}
                try{SystemResources.CreateMenuSound(menu,SystemMenuSound.Select,nativeBudgetBytes:0);return 24;}catch(ArgumentOutOfRangeException){}
                // A raw generated wave exercises canonicalization and native
                // ownership without asserting any authentic menu mapping.
                using(var selected=FirmwareMenuWaveSource.Capture(image.Data,menu.MenuAudio.Archives[0],2,default))
                using(var isolated=Sound.FromEncoded(selected.Header,AudioFormat.JAudioWaveBank,selected.Body))
                {
                    // An original generated recipe reaches every new AOT/native
                    // preparation operation without qualifying synthetic firmware.
                    var bank=MenuBankFixture.Create();
                    var option=FirmwareMenuRecipe.Compile(new byte[]{0xac,6,0,9,0x30,1,100,0x80,30,0x81,0xff},0,MenuBankFixture.Create(12));
                    var cancel=FirmwareMenuRecipe.Compile(new byte[]{0xac,6,0,3,0x30,1,100,0x80,30,0x81,0xff},0,bank);
                    if(option.Frames!=4000||option.SourceWaveIds[0]!=11||cancel.Frames!=5440||cancel.SourceWaveIds[0]!=6)return 35;
                    var recipe=FirmwareMenuRecipe.Compile(new byte[]{0xac,6,0,7,0x30,1,100,0x80,30,0x81,0xff},0,bank);
                    var packet=recipe.Packet(new[]{selected},default);
                    using(var rendered=global::System.Threading.Tasks.Task.Run(()=>Sound.FromMenuRecipe(packet,1024*1024,default)).Result)
                    {
                        Array.Clear(packet,0,packet.Length);
                        var trigger=new byte[64];if(trigger.Length!=64||rendered.SampleCount!=6560||rendered.Channels!=2)return 31;
                        using(var voice=rendered.Play())if(!voice.IsPlaying)return 32;
                    }
                    selected.Dispose();menu.Dispose();image.Dispose();
                    // The host enables collection on every allocation.
                    var collectionTrigger=new byte[64];if(collectionTrigger.Length!=64)return 28;
                    if(isolated.Channels!=1||isolated.SampleRate!=32000||isolated.SampleCount!=16)return 25;
                    using(var voice=isolated.Play())if(!voice.IsPlaying)return 26;
                }
                try{menu.TryGetMenuSoundInfo(SystemMenuSound.Select,out info);return 27;}catch(ObjectDisposedException){}
            }
            int background=global::System.Threading.Tasks.Task.Run(()=>
            {
                try{using(var startup=SystemStartupRenderer.Load(catalog)){}return 1;}
                catch(InvalidOperationException){return 0;}
            }).Result;
            if(background!=0)return 20;
            var device=GraphicsDevice.Default;int before=device.NativeBytes;
            using(var texture=SystemResources.CreateTexture(catalog,SystemTexture.Environment))
                if(texture.Width!=8||texture.Height!=8||texture.MipLevels!=1||texture.NativeBytes<=0)return 5;
            if(device.NativeBytes!=before)return 6;
            using(var model=SystemResources.CreateModelAsync(catalog,SystemModel.BaseCube).Result)
            {
                if(model.NodeCount!=1||model.MeshCount!=1||model.MaterialCount!=1||model.GetNodeName(0)!="boot_demo_base_cube")return 15;
                using(var instance=model.CreateInstance())
                {
                    var transform=new float[16];instance.GetNodeTransform(0,transform);if(transform[15]!=1)return 16;
                    instance.SetNodeOffset(0,new Vector3(2,3,4),Quaternion.Identity,new Vector3(1,1,1));
                    instance.GetNodeTransform(0,transform);if(transform[12]!=2||transform[13]!=3)return 17;
                    instance.SetMaterialColor(0,new Color32(1,2,3,4));instance.SetTevColor(0,0,-12,20,40,200);
                    instance.SetOpacity(0,.25f);instance.ResetMaterialColors(0);instance.ResetNodeOffset(0);
                    instance.GetNodeTransform(0,transform);if(transform[12]!=0)return 18;
                }
            }
        }
        if(!Storage.Mount(StorageDevice.SdSerialPort2))return 2;
        using(var image=SystemFirmware.Open("sd:/firmware-synthetic.bin"))
        using(var catalog=image.CreateCatalog())
        {
            if(image.Source!=FirmwareSource.ExplicitFile||catalog.Count!=6||catalog.Extract(0)[0]!=65)return 3;
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
