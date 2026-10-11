using Dolphin.Diagnostics;
using Dolphin.Input;
using Dolphin.Graphics;

internal static class Program
{
    private static int Main()
    {
        DebugOverlay.WriteLine("Hello from managed C# on GameCube");
        var rotation=0;
        while(true)
        {
            var buttons=Controller.ReadButtonsDown(0);
            if((buttons&Controller.ButtonStart)!=0)return 0;
            rotation=rotation+1;
            Renderer.PresentDemoFrame(rotation);
        }
    }
}
