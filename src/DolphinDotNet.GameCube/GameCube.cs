namespace DolphinDotNet.GameCube;

/// <summary>Managed GameCube platform surface. Methods are lowered to native runtime intrinsics by the AOT compiler.</summary>
public static class GameCube
{
    public static string Platform => Runtime.Platform;

    /// <summary>Writes a line to the GameCube diagnostic overlay.</summary>
    public static void WriteLine(string text) =>
        throw new PlatformNotSupportedException("GameCube.WriteLine is replaced by the DolphinDotNet AOT compiler.");
}
