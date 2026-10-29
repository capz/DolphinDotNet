namespace Dolphin.Diagnostics;

/// <summary>Text diagnostics drawn over the current GameCube graphics frame.</summary>
public static class DebugOverlay
{
    /// <summary>Show the text log over graphics. Hiding it retains its messages and does not hide performance statistics. Runtime failure screens restore visibility.</summary>
    public static bool TextVisible
    {
        get => NativeDiagnostics.ConsoleVisible() != 0;
        set => NativeDiagnostics.ConsoleSetVisible(value ? 1 : 0);
    }
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
    internal static int ConsoleVisible() =>
        throw new System.PlatformNotSupportedException("Text diagnostics require DolphinDotNet AOT.");
    internal static void ConsoleSetVisible(int visible) =>
        throw new System.PlatformNotSupportedException("Text diagnostics require DolphinDotNet AOT.");
    internal static int PerformanceVisible() =>
        throw new System.PlatformNotSupportedException("Performance diagnostics require DolphinDotNet AOT.");
    internal static void PerformanceSetVisible(int visible) =>
        throw new System.PlatformNotSupportedException("Performance diagnostics require DolphinDotNet AOT.");
}
