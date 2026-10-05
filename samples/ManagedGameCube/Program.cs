using DolphinDotNet.GameCube;

internal static class Program
{
    private static int Main()
    {
        GameCube.WriteLine("Hello from managed C# on GameCube");
        var buttons = GameCube.ReadButtonsDown(0);
        return GameCube.Platform.Length > 0 ? buttons : -1;
    }
}
