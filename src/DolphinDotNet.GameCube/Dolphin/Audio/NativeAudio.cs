using System;
namespace Dolphin.Audio;
internal static class NativeAudio
{
    private static Exception Unsupported()=>new PlatformNotSupportedException("Requires a DolphinDotNet native GameCube build.");
    internal static int AudioInit()=>throw Unsupported();
    internal static int AudioBegin(string path)=>throw Unsupported();
    internal static int AudioCreatePcm8(byte[] data,int sampleRate)=>throw Unsupported();
    internal static int AudioStep(int clip)=>throw Unsupported();
    internal static int AudioStat(int clip,int kind)=>throw Unsupported();
    internal static int AudioDispose(int clip)=>throw Unsupported();
    internal static int AudioPlay(int clip,int loop,float volume,float pitch)=>throw Unsupported();
    internal static int AudioVoiceStat(int voice)=>throw Unsupported();
    internal static int AudioVoiceControl(int voice,int kind,float value)=>throw Unsupported();
    internal static int AudioVoiceStop(int voice)=>throw Unsupported();
}
