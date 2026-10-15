using System;
using System.Threading;
using System.Threading.Tasks;
namespace Dolphin.Audio;

/// <summary>Convenient single-track PCM WAV playback. The clip is memory-resident; compressed/streaming music is not implemented.</summary>
public sealed class Music : IDisposable
{
    private readonly Sound sound;
    private SoundChannel? playing;
    private bool disposed;
    private Music(Sound sound){this.sound=sound;}
    public static Music Load(string path)=>new Music(Sound.Load(path));
    public static async Task<Music> LoadAsync(string path,CancellationToken cancellationToken=default)
        =>new Music(await Sound.LoadAsync(path,cancellationToken));
    public float Volume{get{Check();return sound.Volume;}set{Check();sound.Volume=value;if(playing!=null&&playing.IsPlaying)playing.Volume=value;}}
    public float Pitch{get{Check();return sound.Pitch;}set{Check();sound.Pitch=value;if(playing!=null&&playing.IsPlaying)playing.Pitch=value;}}
    public bool IsPlaying{get{Check();return playing!=null&&playing.IsPlaying;}}
    public void Play(bool loop=true)
    {
        Check();
        Stop();
        playing=sound.Play(loop);
    }
    public void Stop(){if(playing!=null){playing.Stop();playing=null;}}
    private void Check(){if(disposed)throw new ObjectDisposedException("Music");}
    public void Dispose(){if(disposed)return;disposed=true;Stop();sound.Dispose();}
}
