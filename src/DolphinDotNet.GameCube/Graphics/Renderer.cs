namespace Dolphin.Graphics;

/// <summary>GameCube graphics entry points.</summary>
public static class Renderer
{
    /// <summary>Draws and presents the built-in rotating demonstration scene.</summary>
    public static void PresentDemoFrame(int rotationDegrees) =>
        throw new System.PlatformNotSupportedException("Renderer.PresentDemoFrame requires DolphinDotNet AOT.");
}
