using Dolphin.Diagnostics;
using Dolphin.Input;
using System.Threading.Tasks;

internal static class Program
{
    private static bool benchmarkTestComplete;
    private static int Main()
    {
        bool benchmark = false;
        while (true)
        {
            if (benchmark ? RunBenchmark() : RunStress()) return 0;
            benchmark = !benchmark;
        }
    }
    private static bool RunStress()
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
        bool automatic = true, exiting = false, switching = false;
        while (true)
        {
            Controller.Update();
            var pad = Controller.GetState(0);
            if (pad.B.WasPressed) exiting = true;
            if (pad.Start.WasPressed) switching = true;
#if DND_TEXTURE_BENCH_CI
            if (!benchmarkTestComplete && scene.Frame >= 360) switching = true;
#endif
            if (exiting || switching)
            {
                if (scene.Stop()) return exiting;
                // Poll without blocking the application executor or starting new work.
                scene.Present();
                Task.Delay(1).GetAwaiter().GetResult();
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
            // Bounded frame pacing hands off the execution gate. The source
            // task itself is only observed once complete, never waited upon.
            Task.Delay(1).GetAwaiter().GetResult();
        }
    }
    private static bool RunBenchmark()
    {
        using var scene = new BenchmarkScene();
        bool exiting = false, switching = false;
        while (true)
        {
            Controller.Update(); var pad = Controller.GetState(0);
            if (pad.B.WasPressed) exiting = true;
            if (pad.Start.WasPressed) switching = true;
#if DND_TEXTURE_BENCH_CI
            if (scene.Completed && scene.FramesAfterDone >= 60)
            { benchmarkTestComplete = true; switching = true; }
#endif
            if (exiting || switching)
            {
                if (scene.Stop()) return exiting;
            }
            else scene.Input(pad);
            scene.Present();
            Task.Delay(1).GetAwaiter().GetResult();
        }
    }
}
