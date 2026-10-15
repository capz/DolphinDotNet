using System;

namespace Dolphin;

/// <summary>Controls what the console attempts to boot after a reset.</summary>
public enum RebootMode
{
    Restart = 0,
    ResetConsole = 1,
    SystemMenu = 2
}

public static class Platform
{
    public const string Name = DolphinDotNet.Runtime.Platform;

    /// <summary>Physical reset button, not the controller Start button.</summary>
    public static bool ResetButtonPressed => NativePlatform.PlatformResetButton();

    /// <summary>Ask libogc2 to restart. Finish saves before calling; bootloaders
    /// decide what runs next. Throws if a reset unexpectedly returns.</summary>
    public static void Reboot(RebootMode mode = RebootMode.Restart)
    {
        if (mode < RebootMode.Restart || mode > RebootMode.SystemMenu)
            throw new ArgumentOutOfRangeException("mode");
        if (!NativePlatform.PlatformReset((int)mode))
            throw new InvalidOperationException("Platform reset did not transfer control.");
    }
}
