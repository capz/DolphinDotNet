using System;
using Dolphin.Audio;

/// <summary>Unsigned PCM8 effects generated on the console; no external assets.</summary>
internal sealed class GreenMenSounds : IDisposable
{
    private readonly Sound shot,hit,damage;
    private SoundChannel? shotVoice,hitVoice,damageVoice;
    internal GreenMenSounds()
    {
        shot=Sound.FromPcm8(GreenMenWaveforms.Pulse(1400,380,65),GreenMenWaveforms.Rate);
        try { hit=Sound.FromPcm8(GreenMenWaveforms.Pulse(360,160,85),GreenMenWaveforms.Rate); }
        catch { shot.Dispose();throw; }
        try { damage=Sound.FromPcm8(GreenMenWaveforms.Pulse(190,60,260),GreenMenWaveforms.Rate); }
        catch { hit.Dispose();shot.Dispose();throw; }
    }
    internal void Play(GreenMenEvent events)
    {
        if((events&GreenMenEvent.Shot)!=0) { shotVoice?.Dispose();shotVoice=shot.Play(); }
        if((events&GreenMenEvent.AlienHit)!=0) { hitVoice?.Dispose();hitVoice=hit.Play();SampleFeedback.Pulse(.05f); }
        if((events&GreenMenEvent.PlayerHit)!=0) { damageVoice?.Dispose();damageVoice=damage.Play();SampleFeedback.Pulse(.18f); }
    }
    public void Dispose()
    {
        shotVoice?.Dispose();hitVoice?.Dispose();damageVoice?.Dispose();
        SampleFeedback.Stop();shot.Dispose();hit.Dispose();damage.Dispose();
    }
}
