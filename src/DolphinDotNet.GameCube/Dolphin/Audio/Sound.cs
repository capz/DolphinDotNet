using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
namespace Dolphin.Audio;

/// <summary>A resident native clip, shared by independent playback instances.</summary>
public sealed class Sound : IDisposable
{
    private readonly int token;
    private bool disposed;
    private float volume=1f,pitch=1f;
    private Sound(int token){this.token=token;}
    /// <summary>Create a self-contained sound from mono, unsigned 8-bit PCM.
    /// Copies the data to aligned native memory, so callers can reuse the array.</summary>
    public static Sound FromPcm8(byte[] samples,int sampleRate=22050)
    {
        if(samples==null)throw new ArgumentNullException("samples");
        if(samples.Length==0)throw new ArgumentException("PCM must not be empty.","samples");
        if(sampleRate<8000 || sampleRate>48000)throw new ArgumentOutOfRangeException("sampleRate");
        Audio.Initialize();
        int token=NativeAudio.AudioCreatePcm8(samples,sampleRate);
        if(token==0)throw new OutOfMemoryException("No free native sound slot or insufficient audio memory.");
        return new Sound(token);
    }
    /// <summary>Load using automatic format detection with cancellation.</summary>
    public static Task<Sound> LoadAsync(string path,CancellationToken cancellation)
        =>LoadAsync(path,cancellationToken:cancellation);
    /// <summary>Load audio using signature detection or an explicit format. Paired wave banks require dataPath; entryIndex is zero-based.
    /// Compressed data stays compressed in native RAM and is decoded into bounded AESND buffers.</summary>
    public static Task<Sound> LoadAsync(string path,AudioFormat format=AudioFormat.Auto,string? dataPath=null,int entryIndex=0,CancellationToken cancellationToken=default,int nativeBudgetBytes=8*1024*1024)
    {
        ValidateSource(path,format,entryIndex,nativeBudgetBytes);
        var load=new LoadWork(path,cancellationToken,format,dataPath,entryIndex,nativeBudgetBytes);
        Task.Run(load.Step);return load.Completion.Task;
    }
    /// <summary>Synchronous counterpart to LoadAsync.</summary>
    public static Sound Load(string path,AudioFormat format=AudioFormat.Auto,string? dataPath=null,int entryIndex=0,int nativeBudgetBytes=8*1024*1024)
    {
        ValidateSource(path,format,entryIndex,nativeBudgetBytes);Audio.Initialize();
        int token=NativeAudio.AudioBeginSource(path,dataPath,(int)format,entryIndex,nativeBudgetBytes);
        if(token==0)throw new IOException("Cannot open audio source or native budget exhausted.");
        try{int status;while((status=NativeAudio.AudioStep(token))==0){}if(status<0)throw new IOException("Invalid or unsupported audio data.");return new Sound(token);}
        catch{NativeAudio.AudioDispose(token);throw;}
    }
    private static void ValidateSource(string path,AudioFormat format,int entry,int budget)
    {
        if(path==null)throw new ArgumentNullException("path");
        if((int)format<0||(int)format>13)throw new ArgumentOutOfRangeException("format");
        if(entry<0||entry>65535)throw new ArgumentOutOfRangeException("entryIndex");
        if(budget<1024||budget>16*1024*1024)throw new ArgumentOutOfRangeException("nativeBudgetBytes");
    }
    private sealed class LoadWork
    {
        private readonly string path;
        private readonly AudioFormat format;
        private readonly string? dataPath;
        private readonly int entry,budget;
        private readonly CancellationToken cancellation;
        private int token;
        internal readonly TaskCompletionSource<Sound> Completion=new TaskCompletionSource<Sound>();
        internal LoadWork(string path,CancellationToken cancellation,AudioFormat format=AudioFormat.Auto,string? dataPath=null,int entry=0,int budget=8*1024*1024){this.path=path;this.cancellation=cancellation;this.format=format;this.dataPath=dataPath;this.entry=entry;this.budget=budget;}
        internal void Step()
        {
            try
            {
                cancellation.ThrowIfCancellationRequested();
                if(token==0)
                {
                    Audio.Initialize();
                    token=NativeAudio.AudioBeginSource(path,dataPath,(int)format,entry,budget);
                    if(token==0)throw new IOException("Cannot open audio source, unsupported encoding, or native memory exhausted.");
                }
                int status=NativeAudio.AudioStep(token);
                if(status<0)throw new IOException("Invalid audio data or native read error.");
                if(status==0){Task.Run(Step);return;}
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
    public int Channels{get{Check();return NativeAudio.AudioStat(token,3);}}
    public int SampleCount{get{Check();return NativeAudio.AudioStat(token,4);}}
    public bool HasLoop{get{Check();return NativeAudio.AudioStat(token,5)!=0;}}
    public int LoopStartSample{get{Check();return NativeAudio.AudioStat(token,6);}}
    public int LoopEndSample{get{Check();return NativeAudio.AudioStat(token,7);}}
    public int NativeBytes{get{Check();return NativeAudio.AudioStat(token,8);}}
    public float Volume{get=>volume;set{Check();ValidateVolume(value);volume=value;}}
    public float Pitch{get=>pitch;set{Check();ValidatePitch(value);pitch=value;}}
    public SoundChannel Play(bool loop=false)
    {
        Check();
        int voice=NativeAudio.AudioPlay(token,loop?1:0,volume,pitch);
        if(voice==0)throw new InvalidOperationException("No free audio voice or invalid hardware pitch.");
        return new SoundChannel(voice,volume,pitch);
    }
    /// <summary>Play the intro, then repeat [loopStartSample, loopEndSample).
    /// Indices count sample frames per channel; the end is exclusive. Overrides
    /// this voice only. Unavailable for WAV and generated PCM.</summary>
    public SoundChannel PlayLoop(int loopStartSample,int loopEndSample)
    {
        ValidateLoopRange(loopStartSample,loopEndSample);
        int voice=NativeAudio.AudioPlayRange(token,loopStartSample,loopEndSample,volume,pitch);
        if(voice==0)throw new InvalidOperationException("No free audio voice or invalid hardware pitch.");
        return new SoundChannel(voice,volume,pitch);
    }
    internal void ValidateLoopRange(int loopStartSample,int loopEndSample)
    {
        Check();
        int count=SampleCount;
        if(loopStartSample<0||loopStartSample>=count)throw new ArgumentOutOfRangeException("loopStartSample");
        if(loopEndSample<=loopStartSample||loopEndSample>count)throw new ArgumentOutOfRangeException("loopEndSample");
        if(NativeAudio.AudioStat(token,9)==0)throw new NotSupportedException("Custom loop ranges are unavailable for WAV and generated PCM.");
    }
    internal static void ValidateVolume(float v)
    {
        if(!(v>=0 && v<=1))throw new ArgumentOutOfRangeException("volume");
    }
    internal static void ValidatePitch(float v)
    {
        if(!(v>=0.25f && v<=3f))throw new ArgumentOutOfRangeException("pitch");
    }
    private void Check(){if(disposed)throw new ObjectDisposedException("Sound");}
    /// <summary>Stops active instances of this clip before freeing native storage.</summary>
    public void Dispose()
    {
        if(disposed)return;
        disposed=true;
        NativeAudio.AudioDispose(token);
    }
}
