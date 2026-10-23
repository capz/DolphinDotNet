using Dolphin.Diagnostics;

internal static class Program
{
    private static int Main()
    {
        DebugOverlay.WriteLine("Hello from real C#!");
        DebugOverlay.WriteLine("Running through DolphinDotNet.");
        DebugOverlay.WriteLine($"ANSWER: {40+2:D2}   PI: {System.Math.PI:F3}");
        return 0;
    }
}
