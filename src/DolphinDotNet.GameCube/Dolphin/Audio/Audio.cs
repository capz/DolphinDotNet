using System;
namespace Dolphin.Audio;

/// <summary>GameCube DSP audio output backed by libogc2 AESND.</summary>
public static class Audio
{
    /// <summary>Initialize before playing audio. Initialize ARAM first if the application uses ARAM.</summary>
    public static void Initialize()
    {
        if(NativeAudio.AudioInit()!=1)
            throw new InvalidOperationException("GameCube audio could not be initialized.");
    }
}
