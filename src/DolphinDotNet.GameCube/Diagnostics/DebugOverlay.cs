namespace Dolphin.Diagnostics;

/// <summary>Text diagnostics drawn over the current GameCube graphics frame.</summary>
public static class DebugOverlay
{
    public static void WriteLine(string text) =>
        throw new System.PlatformNotSupportedException("DebugOverlay.WriteLine requires DolphinDotNet AOT.");
}
