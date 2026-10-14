using Dolphin.Diagnostics;
using Dolphin.Input;

internal static class Program
{
    private static int Main()
    {
        DebugOverlay.WriteLine("Hello from managed C# on GameCube");
        var rotation = 0;
        // Graphics and controller calls stay on the application executor.
        // A game using async work alongside this loop needs no task/IO pumps.
        while (true)
        {
            Controller.Update();
            var pad = Controller.GetState();
            if (pad.Start.WasPressed) return 0;
            rotation = (rotation + 1) % 360;
            DemoScene.PresentDemoFrame(rotation);
        }
    }
}
