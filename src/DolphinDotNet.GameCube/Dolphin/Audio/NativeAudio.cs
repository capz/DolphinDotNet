using System;
namespace Dolphin.Audio;
internal static class NativeAudio
{
    private static Exception Unsupported()=>new PlatformNotSupportedException("Requires a DolphinDotNet native GameCube build.");
    internal static int AudioSystemBegin(byte[] packet,int budget)=>throw Unsupported();
    internal static int AudioSystemStep(int token)=>throw Unsupported();
    internal static int AudioSystemStart(int token,int startup)=>throw Unsupported();
    internal static int AudioSystemPlay(int token,int kind,int value)=>throw Unsupported();
    internal static int AudioSystemControl(int token,int kind,float value)=>throw Unsupported();
    internal static int AudioSystemStat(int token,int kind)=>throw Unsupported();
    internal static int AudioSystemOwner(int token,int owner)=>throw Unsupported();
    internal static void AudioSystemStopOwner(int token,int owner)=>throw Unsupported();
    internal static void AudioSystemStop(int token)=>throw Unsupported();
    internal static void AudioSystemDispose(int token)=>throw Unsupported();
    internal static int AudioInit()=>throw Unsupported();
    internal static int AudioBegin(string path)=>throw Unsupported();
    internal static int AudioBeginMemory(byte[] header,byte[]? body,int format,int entry,int budget)=>throw Unsupported();
    internal static int AudioBeginMenu(byte[] recipe,int budget)=>throw Unsupported();
    internal static int AudioBeginSource(string path,string? dataPath,int format,int entryIndex,int nativeBudgetBytes)=>throw Unsupported();
    internal static int AudioCreatePcm8(byte[] data,int sampleRate)=>throw Unsupported();
    internal static int AudioStep(int clip)=>throw Unsupported();
    internal static int AudioStat(int clip,int kind)=>throw Unsupported();
    internal static int AudioDispose(int clip)=>throw Unsupported();
    internal static int AudioPlay(int clip,int loop,float volume,float pitch)=>throw Unsupported();
    internal static int AudioPlayRange(int clip,int startSample,int endSample,float volume,float pitch)=>throw Unsupported();
    internal static int AudioVoiceStat(int voice)=>throw Unsupported();
    internal static int AudioVoiceControl(int voice,int kind,float value)=>throw Unsupported();
    internal static int AudioVoiceStop(int voice)=>throw Unsupported();
}
