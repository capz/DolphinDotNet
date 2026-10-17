namespace Dolphin.Diagnostics;

/// <summary>Text diagnostics drawn over the current GameCube graphics frame.</summary>
public static class DebugOverlay
{
    /// <summary>Enables the bottom-left performance, memory, BBA and last-reported debugger panel. Disabled by default.</summary>
    public static bool PerformanceVisible
    {
        get => NativeDiagnostics.PerformanceVisible() != 0;
        set => NativeDiagnostics.PerformanceSetVisible(value ? 1 : 0);
    }

    public static void WriteLine(string text) =>
        throw new System.PlatformNotSupportedException("DebugOverlay.WriteLine requires DolphinDotNet AOT.");
}

internal static class NativeDiagnostics
{
    internal static int PerformanceVisible() =>
        throw new System.PlatformNotSupportedException("Performance diagnostics require DolphinDotNet AOT.");
    internal static void PerformanceSetVisible(int visible) =>
        throw new System.PlatformNotSupportedException("Performance diagnostics require DolphinDotNet AOT.");
}
