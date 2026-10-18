using System;
using Dolphin.Audio;

/// <summary>Three synthesized effects live entirely in the DOL; no SD card required.</summary>
internal sealed class PongSounds : IDisposable
{
    private readonly Sound paddle;
    private readonly Sound wall;
    private readonly Sound score;
    private SoundChannel? paddleVoice,wallVoice,scoreVoice;

    internal PongSounds()
    {
        paddle=Sound.FromPcm8(PongWaveforms.Chirp(810,590,70,93),PongWaveforms.Rate);
        try { wall=Sound.FromPcm8(PongWaveforms.Chirp(390,335,55,70),PongWaveforms.Rate); }
        catch { paddle.Dispose();throw; }
        try { score=Sound.FromPcm8(PongWaveforms.Chirp(530,155,285,100),PongWaveforms.Rate); }
        catch { wall.Dispose();paddle.Dispose();throw; }
    }

    internal void Play(PongEvent events)
    {
        if((events&PongEvent.Point)!=0)
        {
            scoreVoice?.Dispose();
            scoreVoice=score.Play();
        }
        else if((events&PongEvent.Paddle)!=0)
        {
            paddleVoice?.Dispose();
            paddleVoice=paddle.Play();SampleFeedback.Pulse(.035f,0);SampleFeedback.Pulse(.035f,1);
        }
        else if((events&PongEvent.Wall)!=0)
        {
            wallVoice?.Dispose();
            wallVoice=wall.Play();
        }
    }
    public void Dispose()
    {
        paddleVoice?.Dispose();wallVoice?.Dispose();scoreVoice?.Dispose();
        SampleFeedback.Stop();paddle.Dispose();wall.Dispose();score.Dispose();
    }
}
