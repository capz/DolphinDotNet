namespace DolphinDotNet.GameCube;

/// <summary>Managed GameCube platform surface. Methods are lowered to native runtime intrinsics by the AOT compiler.</summary>
public static class GameCube
{
    public const int ButtonStart = 0x1000;
    public static string Platform => Runtime.Platform;

    /// <summary>Writes a line to the GameCube diagnostic overlay.</summary>
    public static void WriteLine(string text) =>
        throw new System.PlatformNotSupportedException("GameCube.WriteLine is replaced by the DolphinDotNet AOT compiler.");

    /// <summary>Polls the GameCube controllers and returns the buttons newly pressed on the selected port.</summary>
    public static void PresentDemoFrame(int rotationDegrees) =>
        throw new System.PlatformNotSupportedException("GameCube.PresentDemoFrame is replaced by the DolphinDotNet AOT compiler.");

    public static int ReadButtonsDown(int port = 0) =>
        throw new System.PlatformNotSupportedException("GameCube.ReadButtonsDown is replaced by the DolphinDotNet AOT compiler.");
}
