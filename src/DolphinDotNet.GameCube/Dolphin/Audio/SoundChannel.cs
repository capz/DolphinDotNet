using System;
namespace Dolphin.Audio;

/// <summary>A single playback instance. A finished instance is not reusable.</summary>
public sealed class SoundChannel : IDisposable
{
    private readonly int voice;
    private bool stopped;
    private float volume,pitch;
    internal SoundChannel(int voice,float volume,float pitch)
    { this.voice=voice;this.volume=volume;this.pitch=pitch; }
    public bool IsPlaying => !stopped && NativeAudio.AudioVoiceStat(voice)!=0;
    public float Volume
    {
        get=>volume;
        set {
            Sound.ValidateVolume(value);
            Check();
            if(NativeAudio.AudioVoiceControl(voice,0,value)<0)
                throw new InvalidOperationException("Playback has finished.");
            volume=value;
        }
    }
    public float Pitch
    {
        get=>pitch;
        set {
            Sound.ValidatePitch(value);
            Check();
            if(NativeAudio.AudioVoiceControl(voice,1,value)<0)
                throw new InvalidOperationException("Playback has finished or pitch exceeds the hardware frequency limit.");
            pitch=value;
        }
    }
    private void Check(){if(stopped)throw new ObjectDisposedException("SoundChannel");}
    public void Stop()
    {
        if(stopped)return;
        stopped=true;
        NativeAudio.AudioVoiceStop(voice);
    }
    public void Dispose()=>Stop();
}
