using DolphinDotNet.GameCube;

internal static class Program
{
    private static int Main()
    {
        GameCube.WriteLine("Hello from managed C# on GameCube");
        return GameCube.Platform.Length > 0 ? 0 : 1;
    }
}
