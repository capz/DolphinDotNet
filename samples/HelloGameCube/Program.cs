using Dolphin.Diagnostics;

internal static class Program
{
    private static int Main()
    {
        DebugOverlay.WriteLine("Hello from real C#!");
        DebugOverlay.WriteLine("Running through DolphinDotNet.");
        DebugOverlay.WriteLine((40 + 2).ToString());
        return 0;
    }
}
