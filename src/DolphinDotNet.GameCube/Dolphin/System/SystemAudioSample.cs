using System;
using Dolphin.Audio;

namespace Dolphin.System;

/// <summary>An owned original instrument sample. Play automatically honors
/// its embedded loop; no caller loop flags, endpoints or scheduling are needed.
/// A raw sample has no sequenced instrument envelope or menu-event semantics.</summary>
public sealed class SystemAudioSample : IDisposable
{
    private readonly Sound sound;
    public FirmwareAudioSampleInfo Info {get;}
    internal SystemAudioSample(FirmwareAudioSampleInfo info,Sound sound){Info=info;this.sound=sound;}
    public float Volume {get=>sound.Volume;set=>sound.Volume=value;}
    public float Pitch {get=>sound.Pitch;set=>sound.Pitch=value;}
    public int NativeBytes=>sound.NativeBytes;
    /// <summary>Play the intro once and repeat the source interval when one
    /// exists. Non-looping sources play once. Stop/dispose the returned channel
    /// to end playback. Each call creates an independent playback instance.</summary>
    public SoundChannel Play()=>sound.Play(Info.HasLoop);
    /// <summary>Stops every playback of this sample and frees its native data.</summary>
    public void Dispose()=>sound.Dispose();
}
