using System;
using System.Threading;
using System.Threading.Tasks;
namespace Dolphin.Audio;

/// <summary>Single-track native audio playback. Sources are resident in native memory.</summary>
public sealed class Music : IDisposable
{
    private readonly Sound sound;
    private SoundChannel? playing;
    private bool disposed;
    private Music(Sound sound){this.sound=sound;}
    public static Music Load(string path)=>new Music(Sound.Load(path));
    public static async Task<Music> LoadAsync(string path,CancellationToken cancellationToken=default)
        =>new Music(await Sound.LoadAsync(path,cancellationToken));
    public static Music LoadGameCube(string path,GameCubeAudioFormat format=GameCubeAudioFormat.Auto,string? dataPath=null,int entryIndex=0,int nativeBudgetBytes=8*1024*1024)
        =>new Music(Sound.LoadGameCube(path,format,dataPath,entryIndex,nativeBudgetBytes));
    public static async Task<Music> LoadGameCubeAsync(string path,GameCubeAudioFormat format=GameCubeAudioFormat.Auto,string? dataPath=null,int entryIndex=0,CancellationToken cancellationToken=default,int nativeBudgetBytes=8*1024*1024)
        =>new Music(await Sound.LoadGameCubeAsync(path,format,dataPath,entryIndex,cancellationToken,nativeBudgetBytes));
    public float Volume{get{Check();return sound.Volume;}set{Check();sound.Volume=value;if(playing!=null&&playing.IsPlaying)playing.Volume=value;}}
    public float Pitch{get{Check();return sound.Pitch;}set{Check();sound.Pitch=value;if(playing!=null&&playing.IsPlaying)playing.Pitch=value;}}
    public bool IsPlaying{get{Check();return playing!=null&&playing.IsPlaying;}}
    public void Play(bool loop=true)
    {
        Check();
        Stop();
        playing=sound.Play(loop);
    }
    /// <summary>Restart with an intro followed by a custom loop interval, in sample
    /// frames per channel with an exclusive end. Requires LoadGameCube/Async.</summary>
    public void PlayLoop(int loopStartSample,int loopEndSample)
    {
        Check();
        sound.ValidateLoopRange(loopStartSample,loopEndSample);
        Stop();
        playing=sound.PlayLoop(loopStartSample,loopEndSample);
    }
    public void Stop(){if(playing!=null){playing.Stop();playing=null;}}
    private void Check(){if(disposed)throw new ObjectDisposedException("Music");}
    public void Dispose(){if(disposed)return;disposed=true;Stop();sound.Dispose();}
}
