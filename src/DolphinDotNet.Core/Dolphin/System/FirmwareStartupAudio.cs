using System;

namespace Dolphin.System;

/// <summary>Startup selections verified through the original IPL controller
/// path. Values are stable SDK identities, not instrument program numbers.</summary>
public enum SystemStartupSoundVariant { Normal, OneControllerZ, FourControllerZ }

/// <summary>Immutable metadata for an original paired startup source. A raw
/// stereo resource does not include the persistent score's handover to ambience.</summary>
public sealed class SystemStartupSoundInfo
{
    public SystemStartupSoundVariant Variant {get;}
    public string ProfileId {get;}
    public int Channels=>2;
    public int SampleRate {get;}
    public int SampleCount {get;}
    public double DurationSeconds=>(double)SampleCount/SampleRate;
    public int LeftSourceWaveId {get;}
    public int RightSourceWaveId {get;}
    internal readonly int LeftIndex,RightIndex;
    internal SystemStartupSoundInfo(SystemStartupSoundVariant variant,string profile,int rate,int count,
        int leftId,int rightId,int leftIndex,int rightIndex)
    {Variant=variant;ProfileId=profile;SampleRate=rate;SampleCount=count;LeftSourceWaveId=leftId;RightSourceWaveId=rightId;LeftIndex=leftIndex;RightIndex=rightIndex;}
    internal static void Validate(SystemStartupSoundVariant variant)
    {if(variant<SystemStartupSoundVariant.Normal||variant>SystemStartupSoundVariant.FourControllerZ)throw new ArgumentOutOfRangeException(nameof(variant));}
}
