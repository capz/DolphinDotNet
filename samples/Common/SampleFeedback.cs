using System;
using Dolphin.Audio;
using Dolphin.Input;

internal static class SampleFeedback
{
    internal static void Pulse(float seconds,int port=0)
    {if(Controller.GetState(port).SupportsRumble)Controller.Rumble(port,seconds);}
    internal static void Stop(){for(int i=0;i<Controller.PortCount;i++)Controller.StopRumble(i);}
}

internal sealed class SampleEngine : IDisposable
{
    private readonly Sound engine,cue;
    private readonly SoundChannel voice;
    private SoundChannel? effect;
    internal SampleEngine()
    {
        Audio.Initialize();var wave=new byte[256];
        for(int i=0;i<wave.Length;i++)wave[i]=(byte)(128+(i%64<32?12:-12));
        engine=Sound.FromPcm8(wave,8000);
        var chirp=new byte[960];
        for(int i=0;i<chirp.Length;i++)chirp[i]=(byte)(128+(i%32<16?45:-45)*(960-i)/960);
        try{cue=Sound.FromPcm8(chirp,8000);}
        catch{engine.Dispose();throw;}
        try{voice=engine.Play(true);voice.Volume=.12f;}catch{cue.Dispose();engine.Dispose();throw;}
    }
    internal void Update(float speed,bool running,bool boosted)
    {
        if(!voice.IsPlaying)return;
        voice.Volume=running?(boosted?.22f:.12f):0;
        voice.Pitch=.65f+speed/26;
    }
    internal void Cue(){effect?.Dispose();effect=cue.Play();}
    public void Dispose(){effect?.Dispose();voice.Dispose();cue.Dispose();engine.Dispose();SampleFeedback.Stop();}
}
