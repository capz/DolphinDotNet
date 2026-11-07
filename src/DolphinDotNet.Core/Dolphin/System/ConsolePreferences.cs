using System;
using System.IO;
namespace Dolphin.System;
public enum ConsoleSoundMode { Stereo=0, Mono=1 }
/// <summary>Persistent system preferences shared with IPL and subsequent titles.</summary>
public static class ConsolePreferences
{
    public static ConsoleSoundMode SoundMode
    {
        get {int mode=Dolphin.Timing.NativeTiming.ConsoleSoundRead();if(mode<0)throw new IOException("Console sound mode unavailable.");return (ConsoleSoundMode)mode;}
        set {
            if(value!=ConsoleSoundMode.Stereo&&value!=ConsoleSoundMode.Mono)throw new ArgumentOutOfRangeException("value");
            int result=Dolphin.Timing.NativeTiming.ConsoleSoundWrite((int)value);
            if(result<0)throw new IOException(result==-2?"Sound mode changed but SRAM persistence is pending.":"Console sound mode could not be written.");
        }
    }
}
