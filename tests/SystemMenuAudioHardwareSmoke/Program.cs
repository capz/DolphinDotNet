using System;
using System.IO;
using System.Threading.Tasks;
using Dolphin.Audio;
using Dolphin.Diagnostics;
using Dolphin.Graphics;
using Dolphin.Storage;
using Dolphin.System;
using Dolphin.Timing;

// Logging-only acceptance fixture. A missing semantic mapping is a failed
// acceptance run, never a successful test of a guessed raw instrument wave.
internal static class Program
{
    private static FirmwareImage Acquire()
    {
#if FIRMWARE_EXPLICIT_FILE
        return FirmwareImage.Load("sd:/ipl.bin");
#else
        return SystemFirmware.ReadConsole();
#endif
    }
    private static Sound[] Prepare()
    {
        var names=new[]{SystemMenuSound.TurnSide,SystemMenuSound.TurnCenter,SystemMenuSound.Select};
        var sounds=new Sound[3];bool published=false;
        try
        {
            using(var image=Acquire())
            using(var catalog=image.CreateCatalog(FirmwareCatalogOptions.IncludeMenuAudio,default))
            {
                DebugOverlay.WriteLine("MENU AUDIO SOURCE "+(int)image.Source);DebugOverlay.WriteLine(image.Revision);
                for(int i=0;i<names.Length;i++)
                {
                    SystemMenuSoundInfo? info;
                    if(!catalog.TryGetMenuSoundInfo(names[i],out info)||info==null)
                    {
                        DebugOverlay.WriteLine("MENU AUDIO UNAVAILABLE "+(int)names[i]);
                        // Surface the catalog's specific unsupported reason.
                        sounds[i]=SystemResources.CreateMenuSound(catalog,names[i]);
                        throw new NotSupportedException("Menu availability changed during serialized preparation.");
                    }
                    DebugOverlay.WriteLine("MENU AUDIO PROFILE "+info.ProfileId);
                    DebugOverlay.WriteLine("MENU AUDIO FRAMES "+info.SampleFrameCount+" RATE "+info.SampleRate+" CHANNELS "+info.Channels);
                    sounds[i]=SystemResources.CreateMenuSound(catalog,names[i]);
                }
                DebugOverlay.WriteLine("MENU AUDIO RETAINED ENCODED "+catalog.MenuAudioRetainedBytes);
            }
            DebugOverlay.WriteLine("MENU AUDIO IMAGE AND CATALOG DISPOSED");
            published=true;return sounds;
        }
        finally {if(!published)foreach(var sound in sounds)sound?.Dispose();}
    }
    private static void Gap(float seconds)
    {
        var clock=new FrameClock();float elapsed=0;
        while(elapsed<seconds)
        {
            GraphicsDevice.Default.BeginFrame(Color32.Black);GraphicsDevice.Default.EndFrame();elapsed+=clock.Tick();
        }
    }
    private static int Main()
    {
        Sound[]? sounds=null;
        try
        {
#if FIRMWARE_EXPLICIT_FILE
            if(!Storage.Mount(StorageDevice.SdSerialPort2))throw new IOException("Cannot mount explicit firmware volume.");
            DebugOverlay.WriteLine("MENU AUDIO FILE sd:/ipl.bin");
#endif
            sounds=Task.Run(Prepare).Result;
            for(int repeat=0;repeat<3;repeat++)for(int i=0;i<sounds.Length;i++)
            {
                DebugOverlay.WriteLine("MENU AUDIO BEGIN "+i);
                using(var voice=sounds[i].Play())
                {
                    var clock=new FrameClock();float elapsed=0;
                    while(voice.IsPlaying)
                    {
                        GraphicsDevice.Default.BeginFrame(Color32.Black);GraphicsDevice.Default.EndFrame();elapsed+=clock.Tick();
                        if(elapsed>30)throw new IOException("Menu effect exceeded the acceptance duration limit.");
                    }
                }
                DebugOverlay.WriteLine("MENU AUDIO END "+i);Gap(.5f);
            }
            DebugOverlay.WriteLine("MENU AUDIO COMPLETE");
#if FIRMWARE_ACCEPTANCE_HOLD
            // Optional private capture build: keep completion logs/RAM alive
            // for the existing emulator debugger without pausing the audio run.
            while(true)Gap(1);
#else
            return 0;
#endif
        }
        catch(Exception error){DebugOverlay.WriteLine("MENU AUDIO ACCEPTANCE FAILED");DebugOverlay.WriteLine(error.Message);return 1;}
        finally
        {
            if(sounds!=null)foreach(var sound in sounds)sound?.Dispose();
#if FIRMWARE_EXPLICIT_FILE
            if(Storage.IsMounted(StorageDevice.SdSerialPort2))Storage.Unmount(StorageDevice.SdSerialPort2);
#endif
        }
    }
}
