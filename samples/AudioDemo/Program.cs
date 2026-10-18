using System;
using System.Threading;
using System.Threading.Tasks;
using Dolphin.Audio;
using Dolphin.Graphics;
using Dolphin.Input;
using Dolphin.Storage;

internal static class Program
{
    // A disc probe is safe without media. Select a configured SD adapter and
    // matching root explicitly; blind SD adapter probes can stall in Dolphin.
    private const StorageDevice AudioStorage=StorageDevice.DvdDrive;
    private const string AudioRoot="dvd:/audio/";
    private static Sound Tone()
    {
        var pcm=new byte[1600];
        for(int i=0;i<pcm.Length;i++)pcm[i]=(byte)(128+(i%32<16?40:-40)*(1600-i)/1600);
        return Sound.FromPcm8(pcm,8000);
    }
    private static async Task<int> Main()
    {
        bool mounted=AudioStorage==StorageDevice.DvdDrive?Storage.MountDisc():Storage.Mount(AudioStorage);
        Audio.Initialize();
        using var draw=new Draw2D(GraphicsDevice.Default,new Draw2DOptions {Width=640,Height=480});
        using var text=new SampleText();using var cancel=new CancellationTokenSource();
        Task<Music>? loading=mounted?Music.LoadAsync(AudioRoot+"theme.wav",cancel.Token):null;
        Task<Sound>? soundLoading=mounted?Sound.LoadAsync(AudioRoot+"laser.wav",cancel.Token):null;
        Music? music=null;Sound? sound=mounted?null:Tone();SoundChannel? voice=null;
        string status=mounted?"LOADING WAV - B CANCEL":"NO AUDIO DISC - A PLAYS GENERATED TONE";
        float volume=.4f,pitch=1;bool quit=false;Exception? failure=null;
        try
        {
            while(true)
            {
                Controller.Update();var pad=Controller.GetState();
                if(pad.B.WasPressed){quit=true;cancel.Cancel();}
                if(loading!=null&&loading.IsCompleted)
                {
                    try{music=loading.GetAwaiter().GetResult();music.Volume=volume;music.Play();status="MUSIC READY";}
                    catch(Exception error){status=error.Message;}loading=null;
                }
                if(soundLoading!=null&&soundLoading.IsCompleted)
                {try{sound=soundLoading.GetAwaiter().GetResult();}catch(Exception error){status=error.Message;}soundLoading=null;}
                if(quit&&loading==null&&soundLoading==null)break;
                if(pad.DPadUp.WasPressed)volume=Math.Min(1,volume+.1f);
                if(pad.DPadDown.WasPressed)volume=Math.Max(0,volume-.1f);
                if(pad.DPadLeft.WasPressed)pitch=Math.Max(.5f,pitch-.1f);
                if(pad.DPadRight.WasPressed)pitch=Math.Min(2,pitch+.1f);
                if(music!=null){music.Volume=volume;music.Pitch=pitch;}
                if(pad.A.WasPressed&&sound!=null){voice?.Dispose();voice=sound.Play();voice.Volume=volume;voice.Pitch=pitch;}
                if(pad.Start.WasPressed&&music!=null){if(music.IsPlaying)music.Stop();else music.Play();}
                if(pad.X.WasPressed&&loading==null&&mounted)
                {music?.Dispose();music=null;loading=Music.LoadAsync(AudioRoot+"theme.wav",cancel.Token);status="RELOADING WAV";}
                if(pad.Y.WasPressed&&loading==null&&mounted)
                {music?.Dispose();music=null;loading=Music.LoadAsync(AudioRoot+"theme.adx",cancel.Token);status="LOADING ADX - L CUSTOM LOOP";}
                if(pad.L.WasPressed&&music!=null)
                {
                    try{music.PlayLoop(8000,24000);status="CUSTOM LOOP 8000 TO 24000";}
                    catch(Exception error){status=error.Message;}
                }
                var device=GraphicsDevice.Default;device.BeginFrame(new Color32(18,25,40));draw.Begin();
                text.Draw(draw,"AUDIO LAB",24,60,5,Color32.White);
                text.Draw(draw,status.Length>68?status.Substring(0,68):status,24,110,2,Color32.White);
                text.Draw(draw,$"VOLUME {volume:F1}   PITCH {pitch:F1}",24,170,4,Color32.White);
                text.Draw(draw,music!=null&&music.IsPlaying?"PLAYING":"STOPPED",24,220,4,Color32.White);
                text.Draw(draw,"A SFX  START PLAY STOP  DPAD VOLUME PITCH",24,380,2,Color32.White);
                text.Draw(draw,"X RELOAD WAV  Y LOAD ADX  L LOOP  B EXIT",24,410,2,Color32.White);
                draw.End();device.EndFrame();await Task.Delay(16);
            }
        }
        catch(Exception error){failure=error;}
        cancel.Cancel();
        if(loading!=null){try{(await loading).Dispose();}catch(Exception){}}
        if(soundLoading!=null){try{(await soundLoading).Dispose();}catch(Exception){}}
        voice?.Dispose();sound?.Dispose();music?.Dispose();
        if(mounted)Storage.Unmount(AudioStorage);
        if(failure!=null)throw failure;
        return 0;
    }
}
