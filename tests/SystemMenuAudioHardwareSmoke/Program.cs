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
        var names=new[]{SystemMenuSound.TurnSide,SystemMenuSound.TurnCenter,SystemMenuSound.Select,
            SystemMenuSound.Back,SystemMenuSound.OptionCancel,SystemMenuSound.OptionConfirm,
            SystemMenuSound.OptionMove1,SystemMenuSound.OptionMove2,SystemMenuSound.OptionMove3,SystemMenuSound.OptionSelect};
        var sounds=new Sound[names.Length];bool published=false;
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
    private static void SampleAcceptance()
    {
        SystemAudioSample[]? samples=null;
        try
        {
            samples=Task.Run(()=>
            {
                using(var image=Acquire())
                using(var catalog=image.CreateCatalog(FirmwareCatalogOptions.IncludeSystemAudio,default))
                {
                    if(catalog.AudioSampleCount!=12)throw new IOException("Expected twelve original samples.");
                    var prepared=new SystemAudioSample[12];
                    try
                    {
                        for(int i=0;i<12;i++)prepared[i]=SystemResources.CreateAudioSample(catalog,catalog.GetAudioSampleInfo(i));
                        return prepared;
                    }
                    catch {foreach(var sample in prepared)sample?.Dispose();throw;}
                }
            }).Result;
            DebugOverlay.WriteLine("SAMPLE INPUTS DISPOSED COUNT "+samples.Length);
            for(int i=0;i<samples.Length;i++)
            {
                var sample=samples[i];DebugOverlay.WriteLine("SAMPLE BEGIN "+i+" LOOP "+(sample.Info.HasLoop?1:0));
                using(var voice=sample.Play())
                {
                    if(!voice.IsPlaying)throw new IOException("Original sample did not start.");
                    Gap((float)sample.Info.SampleCount/sample.Info.SampleRate+1);
                    if(voice.IsPlaying!=sample.Info.HasLoop)throw new IOException("Original sample automatic loop state mismatch.");
                    voice.Stop();if(voice.IsPlaying)throw new IOException("Original sample did not stop.");
                }
                DebugOverlay.WriteLine("SAMPLE END "+i);
            }
            using(var a=samples[7].Play())
            using(var b=samples[7].Play())
            {
                Gap(1);samples[7].Dispose();
                if(a.IsPlaying||b.IsPlaying)throw new IOException("Sample disposal left active playback.");
            }
        }
        finally {if(samples!=null)foreach(var sample in samples)sample?.Dispose();}
    }
    private sealed class StartupSet : IDisposable
    {
        internal Sound[] Sounds=new Sound[3];
        internal Music[] Music=new Music[3];
        public void Dispose(){foreach(var sound in Sounds)sound?.Dispose();foreach(var music in Music)music?.Dispose();}
    }
    private static void StartupAcceptance()
    {
        using(var prepared=Task.Run(()=>
        {
            var set=new StartupSet();
            try
            {
                using(var image=Acquire())
                using(var catalog=image.CreateCatalog(FirmwareCatalogOptions.IncludeSystemAudio,default))
                {
                    for(int i=0;i<3;i++)
                    {
                        var variant=(SystemStartupSoundVariant)i;
                        SystemStartupSoundInfo? info;
                        if(!catalog.TryGetStartupSoundInfo(variant,out info)||info==null)throw new NotSupportedException("Startup variant unavailable.");
                        int expected=i==0?174067:i==1?166745:187092;
                        if(info.SampleCount!=expected||info.Channels!=2||info.SampleRate!=32000)throw new IOException("Startup variant metadata mismatch.");
                        set.Sounds[i]=SystemResources.CreateStartupVariantSound(catalog,variant);
                        set.Music[i]=SystemResources.CreateStartupVariantMusic(catalog,variant);
                    }
                    // Keep the established default-token call source compatible.
                    using(var legacy=SystemResources.CreateStartupSound(catalog,default))
                        if(legacy.SampleCount!=174067)throw new IOException("Legacy startup changed.");
                }
                return set;
            }
            catch {set.Dispose();throw;}
        }).Result)
        {
            DebugOverlay.WriteLine("STARTUP INPUTS DISPOSED COUNT 3");
            for(int i=0;i<3;i++)
            {
                var sound=prepared.Sounds[i];DebugOverlay.WriteLine("STARTUP VARIANT BEGIN "+i+" FRAMES "+sound.SampleCount);
                using(var voice=sound.Play())
                {
                    if(!voice.IsPlaying||sound.Channels!=2||sound.SampleRate!=32000)throw new IOException("Startup variant did not start.");
                    Gap((float)sound.SampleCount/sound.SampleRate+1);
                    if(voice.IsPlaying)throw new IOException("Startup variant did not finish.");
                }
                var music=prepared.Music[i];music.Play(false);Gap(.25f);
                if(!music.IsPlaying)throw new IOException("Startup variant music did not start.");
                music.Stop();if(music.IsPlaying)throw new IOException("Startup variant music did not stop.");
                DebugOverlay.WriteLine("STARTUP VARIANT END "+i);
            }
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
#if FIRMWARE_STARTUP_ACCEPTANCE
            StartupAcceptance();
            DebugOverlay.WriteLine("STARTUP AUDIO COMPLETE");
#elif FIRMWARE_SAMPLE_ACCEPTANCE
            SampleAcceptance();
            DebugOverlay.WriteLine("SAMPLE AUDIO COMPLETE");
#else
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
#endif
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
