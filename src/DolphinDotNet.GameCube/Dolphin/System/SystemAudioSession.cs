using System;
using System.IO;
using System.Threading;
using Dolphin.Audio;

namespace Dolphin.System;

public enum SystemAudioOutputMode { Stereo=0, Mono=1 }
public enum SystemAudioSessionState { Stopped=0, Playing=1, Faulted=2, Disposed=3 }
/// <summary>Verified controls of the original score. Boolean controls accept 0 or 1.</summary>
public enum SystemAudioControl
{
    AmbienceEnabled=0,
    /// <summary>Original cube-motion intensity/progress, from 0 to 1.</summary>
    CubeMotion=1,
    /// <summary>Original submenu-motion intensity/progress, from 0 to 1.</summary>
    SubmenuMotion=2
}

/// <summary>Snapshot of native session allocation and streaming health.</summary>
public sealed class SystemAudioStatistics
{
    public int NativeBytes {get;}
    public int PeakNativeBytes {get;}
    public int ActiveVoices {get;}
    public int BufferedFrames {get;}
    /// <summary>Frames synthesized since the latest Start or startup reset.</summary>
    public long RenderedFrames {get;}
    public int Underruns {get;}
    public int FaultCode {get;}
    internal SystemAudioStatistics(int bytes,int peak,int voices,int buffered,int underruns,int fault,long rendered)
    {NativeBytes=bytes;PeakNativeBytes=peak;ActiveVoices=voices;BufferedFrames=buffered;Underruns=underruns;FaultCode=fault;RenderedFrames=rendered;}
}

/// <summary>Owns a prepared original IPL score, instruments and native producer.
/// Preparation does not start playback. Playback/control use the application
/// executor. One session may play at a time, alongside ordinary Sound/Music.</summary>
public sealed class SystemAudioSession : IDisposable
{
    private readonly int token;
    private readonly FirmwareAudioSampleInfo[] samples;
    private bool disposed;
    private float volume=1;
    private SystemAudioOutputMode outputMode;
    public string ProfileId {get;}
    public int SampleCount=>samples.Length;
    internal SystemAudioSession(int token,FirmwareSystemAudioSnapshot snapshot)
    {this.token=token;samples=(FirmwareAudioSampleInfo[])snapshot.Samples.Clone();ProfileId=snapshot.ProfileId;}
    internal static SystemAudioSession Prepare(FirmwareSystemAudioSnapshot snapshot,int budget,CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();Dolphin.Audio.Audio.Initialize();
        int native=NativeAudio.AudioSystemBegin(snapshot.Packet,budget);
        if(native==0)throw new IOException("No IPL session slot or insufficient native preparation budget.");
        try
        {
            int status;do{cancellation.ThrowIfCancellationRequested();status=NativeAudio.AudioSystemStep(native);}while(status==0);
            if(status<0)throw new InvalidDataException("Unsupported or malformed IPL score or instrument data.");
            cancellation.ThrowIfCancellationRequested();return new SystemAudioSession(native,snapshot);
        }
        catch {NativeAudio.AudioSystemDispose(native);throw;}
    }
    public FirmwareAudioSampleInfo GetSample(int index)
    {Check();if(index<0||index>=samples.Length)throw new ArgumentOutOfRangeException(nameof(index));return samples[index];}
    public SystemAudioSessionState State
    {
        get
        {
            if(disposed)return SystemAudioSessionState.Disposed;
            int state=NativeAudio.AudioSystemStat(token,0);
            return state==3?SystemAudioSessionState.Faulted:state==2?SystemAudioSessionState.Playing:SystemAudioSessionState.Stopped;
        }
    }
    public bool IsStartupPlaying {get {Check();return NativeAudio.AudioSystemStat(token,10)!=0;}}
    public float Volume
    {
        get{Check();return volume;}
        set{Check();Sound.ValidateVolume(value);Control(0,value);volume=value;}
    }
    /// <summary>Session-local stereo output or equal-channel mono downmix.</summary>
    public SystemAudioOutputMode OutputMode
    {
        get{Check();return outputMode;}
        set
        {
            Check();if(value!=SystemAudioOutputMode.Stereo&&value!=SystemAudioOutputMode.Mono)throw new ArgumentOutOfRangeException(nameof(value));
            Control(1,(float)value);outputMode=value;
        }
    }
    /// <summary>Start the persistent original idle score. Repeated Start calls
    /// leave a running session alone. Stop clears cues and all delay tails.</summary>
    public void Start()
    {
        Check();if(State==SystemAudioSessionState.Playing)return;
        if(NativeAudio.AudioSystemStart(token,-1)==0)throw new InvalidOperationException("IPL session cannot start; another session may be active or this session has faulted.");
    }
    /// <summary>Reset the score, play the selected original startup path, and
    /// start idle ambience automatically when its original sources finish.</summary>
    public void PlayStartup(SystemStartupSoundVariant variant=SystemStartupSoundVariant.Normal)
    {
        Check();if((int)variant<0||(int)variant>2)throw new ArgumentOutOfRangeException(nameof(variant));
        int program=variant==SystemStartupSoundVariant.Normal?0:variant==SystemStartupSoundVariant.OneControllerZ?2:1;
        if(NativeAudio.AudioSystemStart(token,program)==0)throw new InvalidOperationException("IPL startup cannot start; another session may be active or this session has faulted.");
    }
    /// <summary>Play a verified menu cue through the persistent score. Disposing
    /// the returned handle stops its complete track subtree and active notes.</summary>
    public SystemAudioPlayback PlayCue(SystemMenuSound cue)
    {
        Check();FirmwareMenuAudio.Validate(cue);
        return PlayCue((SystemAudioCue)(int)cue);
    }
    public SystemAudioPlayback PlayCue(SystemAudioCue cue)
    {
        Check();SystemAudioCueInfo.Validate(cue);Start();return Play(0,(int)cue);
    }
    /// <summary>Audition an owned source. Its intro and embedded repeat section
    /// are handled automatically; a nonlooping source plays once.</summary>
    public SystemAudioPlayback PlaySample(FirmwareAudioSampleInfo sample)
    {
        Check();if(sample==null)throw new ArgumentNullException(nameof(sample));
        int id=-1;for(int i=0;i<samples.Length;i++)if(ReferenceEquals(samples[i],sample)){id=i;break;}
        if(id<0)throw new ArgumentException("Sample does not belong to this session's snapshot.",nameof(sample));
        Start();return Play(1,id);
    }
    public void SetControl(SystemAudioControl control,float value)
    {
        Check();if((int)control<0||(int)control>2)throw new ArgumentOutOfRangeException(nameof(control));
        if(!(value>=0&&value<=1)||(control==SystemAudioControl.AmbienceEnabled&&value!=0&&value!=1))throw new ArgumentOutOfRangeException(nameof(value));
        Start();Control(2+(int)control,value);
    }
    private void Control(int kind,float value)
    {if(NativeAudio.AudioSystemControl(token,kind,value)==0)throw new InvalidOperationException("IPL audio control failed or the session has faulted.");}
    private SystemAudioPlayback Play(int kind,int value)
    {
        int owner=NativeAudio.AudioSystemPlay(token,kind,value);
        if(owner==0)throw new InvalidOperationException("IPL cue/source cannot play or native voice/track capacity is exhausted.");
        return new SystemAudioPlayback(this,owner);
    }
    public SystemAudioStatistics GetStatistics()
    {
        Check();return new SystemAudioStatistics(NativeAudio.AudioSystemStat(token,1),NativeAudio.AudioSystemStat(token,2),
            NativeAudio.AudioSystemStat(token,3),NativeAudio.AudioSystemStat(token,8),NativeAudio.AudioSystemStat(token,7),NativeAudio.AudioSystemStat(token,9),(long)NativeAudio.AudioSystemStat(token,4)*80);
    }
    public void Stop(){Check();NativeAudio.AudioSystemStop(token);}
    internal bool IsPlaying(int owner)=>!disposed&&NativeAudio.AudioSystemOwner(token,owner)!=0;
    internal void Stop(int owner){if(!disposed)NativeAudio.AudioSystemStopOwner(token,owner);}
    private void Check(){if(disposed)throw new ObjectDisposedException(nameof(SystemAudioSession));}
    public void Dispose(){if(disposed)return;disposed=true;NativeAudio.AudioSystemDispose(token);}
}

/// <summary>Owned playback of one cue or automatically looping source.</summary>
public sealed class SystemAudioPlayback : IDisposable
{
    private readonly SystemAudioSession session;
    private readonly int owner;
    private bool stopped;
    internal SystemAudioPlayback(SystemAudioSession session,int owner){this.session=session;this.owner=owner;}
    public bool IsPlaying=>!stopped&&session.IsPlaying(owner);
    public void Stop(){if(stopped)return;stopped=true;session.Stop(owner);}
    public void Dispose()=>Stop();
}
