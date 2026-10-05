using DolphinDotNet.GameCube;

internal static class Program
{
    private static int Main()
    {
        GameCube.WriteLine("Hello from managed C# on GameCube");
        var rotation=0;
        while(true)
        {
            var buttons=GameCube.ReadButtonsDown(0);
            if((buttons&GameCube.ButtonStart)!=0)return 0;
            rotation=rotation+1;
            GameCube.PresentDemoFrame(rotation);
        }
    }
}
