namespace DolphinDotNet.GameCube;

/// <summary>Managed GameCube platform surface. Methods are lowered to native runtime intrinsics by the AOT compiler.</summary>
public static class GameCube
{
    public static string Platform => Runtime.Platform;
}
