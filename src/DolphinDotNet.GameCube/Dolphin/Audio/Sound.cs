using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Scheduler = Dolphin.Threading.Tasks.Scheduler;
namespace Dolphin.Audio;

/// <summary>One resident PCM WAV clip, shared safely by multiple playback instances.</summary>
public sealed class Sound : IDisposable
{
    private readonly int token;
    private bool disposed;
    private float volume=1f,pitch=1f;
    private Sound(int token){this.token=token;}
    /// <summary>Read a PCM 8/16-bit, mono/stereo WAV into aligned native RAM. The volume must be mounted first.</summary>
    public static Sound Load(string path)
    {
        if(path==null)throw new ArgumentNullException("path");
        Audio.Initialize();
        int token=NativeAudio.AudioBegin(path);
        if(token==0)throw new IOException("Cannot open WAV, unsupported encoding, or native memory exhausted.");
        try
        {
            int status;
            while((status=NativeAudio.AudioStep(token))==0){}
            if(status<0)throw new IOException("Incomplete WAV or native read error.");
            return new Sound(token);
        }
        catch{NativeAudio.AudioDispose(token);throw;}
    }
    /// <summary>Load incrementally in 4 KiB slices through the automatic task scheduler.</summary>
    public static Task<Sound> LoadAsync(string path,CancellationToken cancellationToken=default)
    {
        if(path==null)throw new ArgumentNullException("path");
        var load=new LoadWork(path,cancellationToken);
        Scheduler.Post(load.Step);
        return load.Completion.Task;
    }
    private sealed class LoadWork
    {
        private readonly string path;
        private readonly CancellationToken cancellation;
        private int token;
        internal readonly TaskCompletionSource<Sound> Completion=new TaskCompletionSource<Sound>();
        internal LoadWork(string path,CancellationToken cancellation){this.path=path;this.cancellation=cancellation;}
        internal void Step()
        {
            try
            {
                cancellation.ThrowIfCancellationRequested();
                if(token==0)
                {
                    Audio.Initialize();
                    token=NativeAudio.AudioBegin(path);
                    if(token==0)throw new IOException("Cannot open WAV, unsupported encoding, or native memory exhausted.");
                }
                int status=NativeAudio.AudioStep(token);
                if(status<0)throw new IOException("Incomplete WAV or native read error.");
                if(status==0){Scheduler.Post(Step);return;}
                cancellation.ThrowIfCancellationRequested();
                var sound=new Sound(token);token=0;Completion.SetResult(sound);
            }
            catch(Exception error)
            {
                if(token!=0){NativeAudio.AudioDispose(token);token=0;}
                Completion.SetException(error);
            }
        }
    }
    public int LengthBytes{get{Check();return NativeAudio.AudioStat(token,1);}}
    public int SampleRate{get{Check();return NativeAudio.AudioStat(token,2);}}
    public float Volume{get=>volume;set{Check();ValidateVolume(value);volume=value;}}
    public float Pitch{get=>pitch;set{Check();ValidatePitch(value);pitch=value;}}
    public SoundChannel Play(bool loop=false)
    {
        Check();
        int voice=NativeAudio.AudioPlay(token,loop?1:0,volume,pitch);
        if(voice==0)throw new InvalidOperationException("No free audio voice or invalid hardware pitch.");
        return new SoundChannel(voice,volume,pitch);
    }
    internal static void ValidateVolume(float v)
    {
        if(float.IsNaN(v)||float.IsInfinity(v)||v<0||v>1)throw new ArgumentOutOfRangeException("volume");
    }
    internal static void ValidatePitch(float v)
    {
        if(float.IsNaN(v)||float.IsInfinity(v)||v<0.25f||v>3f)throw new ArgumentOutOfRangeException("pitch");
    }
    private void Check(){if(disposed)throw new ObjectDisposedException("Sound");}
    /// <summary>Stops active instances of this clip before freeing native PCM storage.</summary>
    public void Dispose()
    {
        if(disposed)return;
        disposed=true;
        NativeAudio.AudioDispose(token);
    }
}
