using Dolphin.Diagnostics;
using Dolphin.Input;

internal static class Program
{
    private static int Main()
    {
#if DND_TEXTURE_ATLAS_START
        const bool startAtlas = true;
#else
        const bool startAtlas = false;
#endif
#if DND_TEXTURE_STREAM_CI
        DebugOverlay.PerformanceVisible = false;
#else
        DebugOverlay.PerformanceVisible = true;
#endif
        using var scene = new StrategyScene(startAtlas);
#if DND_TEXTURE_STREAM_CI
        scene.InjectFailures = true;
#endif
        bool automatic = true, exiting = false;
        while (true)
        {
            Controller.Update();
            var pad = Controller.GetState(0);
            if (pad.B.WasPressed) exiting = true;
            if (exiting)
            {
                if (scene.Stop()) return 0;
                // Poll without blocking the application executor or starting new work.
                scene.Present();
                continue;
            }
            if (pad.A.WasPressed) scene.ToggleMode();
            if (pad.Y.WasPressed) automatic = !automatic;
            if (pad.X.WasPressed) scene.InjectFailures = !scene.InjectFailures;
#if DND_TEXTURE_STREAM_CI
            if (scene.Frame > 0 && scene.Frame % 240 == 0) scene.ToggleMode();
#endif
            scene.Move(automatic, pad.Stick.X * .15f, pad.Stick.Y * .15f);
            scene.Present();
            if (scene.Frame % 120 == 0) scene.Report();
        }
    }
}
