namespace Dolphin.Audio;

/// <summary>GameCube audio containers; headerless and paired formats require an explicit choice.</summary>
public enum GameCubeAudioFormat
{
    Auto=0,
    Dsp=1,
    Adx=2,
    Afc=3,
    Hps=4,
    Ssm=5,
    Ast=6,
    JAudioWaveBank=7,
    RenderWareStream=8,
    Afs=9,
    ResidentEvil4Stream=10,
    Dtk=11,
    RenderWareWaveDictionary=12
}
