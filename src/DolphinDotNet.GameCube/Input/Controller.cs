namespace Dolphin.Input;

/// <summary>GameCube controller input.</summary>
public static class Controller
{
    public const int ButtonStart = 0x1000;

    /// <summary>Polls controllers and returns newly pressed buttons on the selected port.</summary>
    public static int ReadButtonsDown(int port = 0) =>
        throw new System.PlatformNotSupportedException("Controller.ReadButtonsDown requires DolphinDotNet AOT.");
}
