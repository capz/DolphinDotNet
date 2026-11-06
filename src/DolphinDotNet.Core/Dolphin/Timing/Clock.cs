using System;

namespace Dolphin.Timing;

/// <summary>Monotonic elapsed-time clock, independent of calendar time. Timestamps use microsecond units.</summary>
public static class Clock
{
    public const long Frequency=1000000;
    public static long Timestamp => NativeTiming.TimingNow();
    public static double ElapsedSeconds(long start, long end)
    {
        if (start<0) throw new ArgumentOutOfRangeException("start");
        if (end<start) throw new ArgumentOutOfRangeException("end");
        return (end-start)/(double)Frequency;
    }
}
internal static class NativeTiming
{
    internal static long TimingNow() => throw new PlatformNotSupportedException("Requires DolphinDotNet AOT.");
    internal static long CalendarRead() => throw new PlatformNotSupportedException("Requires a GameCube RTC.");
    internal static int CalendarWrite(long seconds) => throw new PlatformNotSupportedException("Requires a GameCube RTC.");
    internal static int ConsoleSoundRead() => throw new PlatformNotSupportedException("Requires GameCube SRAM.");
    internal static int ConsoleSoundWrite(int mode) => throw new PlatformNotSupportedException("Requires GameCube SRAM.");
}
