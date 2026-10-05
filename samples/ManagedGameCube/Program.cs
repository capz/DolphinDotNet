using DolphinDotNet.GameCube;

internal static class Program
{
    private static int Main()
    {
        return GameCube.Platform.Length > 0 ? 0 : 1;
    }
}
