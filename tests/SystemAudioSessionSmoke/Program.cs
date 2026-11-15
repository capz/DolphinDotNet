using System;
using System.Threading;
using System.Threading.Tasks;
using Dolphin.Audio;
using Dolphin.Diagnostics;
using Dolphin.System;
using Dolphin.Timing;
using Scheduler=Dolphin.Threading.Tasks.Scheduler;

internal static class Program
{
    private static async Task<int> Main()
    {
        DebugOverlay.WriteLine("IPL SESSION PREPARING");
        Task<SystemAudioSession> preparing;
        using(var image=SystemFirmware.ReadConsole())
        using(var catalog=image.CreateCatalog(FirmwareCatalogOptions.IncludeSystemAudio,CancellationToken.None))
        {
            if(!catalog.SupportsAudioSession)return 1;
            using(var cancel=new CancellationTokenSource())
            {
                cancel.Cancel();
                try{await SystemResources.CreateAudioSessionAsync(catalog,cancel.Token);return 2;}
                catch(OperationCanceledException){}
            }
            preparing=SystemResources.CreateAudioSessionAsync(catalog);
        }
        using(var session=await preparing)
        {
            if(!Scheduler.IsApplicationThread||session.State!=SystemAudioSessionState.Stopped||session.SampleCount!=12)return 3;
            session.Volume=.8f;session.OutputMode=SystemAudioOutputMode.Stereo;
            session.Start();DebugOverlay.WriteLine("IPL SESSION AMBIENCE");
            await Time.DelayAsync(5000);
            for(int i=0;i<10;i++)
            {
                using(var cue=session.PlayCue((SystemMenuSound)i))
                {if(!cue.IsPlaying)return 4;await Time.DelayAsync(650);}
            }
            session.SetControl(SystemAudioControl.AmbienceEnabled,0);
            var loop=session.PlaySample(session.GetSample(10));
            if(!loop.IsPlaying)return 5;
            await Time.DelayAsync(3000);if(!loop.IsPlaying)return 6;
            loop.Stop();if(loop.IsPlaying)return 7;
            session.SetControl(SystemAudioControl.AmbienceEnabled,1);
            using(var ordinary=Sound.FromPcm8(new byte[]{128,140,128,116,128,140,128,116},8000))
            using(var voice=ordinary.Play(true))
            {
                await Time.DelayAsync(1000);if(!voice.IsPlaying)return 8;
            }
            session.OutputMode=SystemAudioOutputMode.Mono;await Time.DelayAsync(1000);
            session.Stop();if(session.State!=SystemAudioSessionState.Stopped)return 9;
            session.PlayStartup(SystemStartupSoundVariant.Normal);await Time.DelayAsync(6500);
            session.PlayStartup(SystemStartupSoundVariant.OneControllerZ);await Time.DelayAsync(6500);
            session.PlayStartup(SystemStartupSoundVariant.FourControllerZ);await Time.DelayAsync(6500);
            var stats=session.GetStatistics();
            DebugOverlay.WriteLine("IPL BYTES "+stats.NativeBytes+" UNDERRUNS "+stats.Underruns+" FAULT "+stats.FaultCode);
            if(stats.Underruns!=0||stats.FaultCode!=0||stats.PeakNativeBytes>8*1024*1024)return 10;
            int background=await Task.Run(()=>{try{session.Start();return 1;}catch(InvalidOperationException){return 0;}});
            if(background!=0)return 11;
            session.Stop();session.Start();await Time.DelayAsync(1000);
        }
        DebugOverlay.WriteLine("IPL SESSION PASS");return 0;
    }
}
