using Dolphin.Diagnostics;
using Dolphin.Input;
using Dolphin.Timing;

internal static class Program
{
    private static int Main()
    {
        DebugOverlay.PerformanceVisible = true;
        DebugOverlay.WriteLine("csharp with dolphin dotnet");
        float rotation=0;
        var clock=new FrameClock { MaximumDeltaSeconds=.1f };
        // Graphics and controller calls stay on the application executor.
        // A game using async work alongside this loop needs no task/IO pumps.
        while (true)
        {
            Controller.Update();
            var pad = Controller.GetState();
            if (pad.Start.WasPressed) return 0;
            rotation = (rotation + 60*clock.Tick()) % 360;
            DemoScene.PresentDemoFrame((int)rotation);
        }
    }
}
