using System;
using Dolphin.Diagnostics;
using Dolphin.Graphics;
using Dolphin.Input;
using Dolphin.Timing;

internal static class Program
{
    private static int Main()
    {
        DebugOverlay.PerformanceVisible=true;
        using var scene=new StrategyScene();
        var clock=new FrameClock { MaximumDeltaSeconds=.1f };
#if DND_TEXTURE_ATLAS_START
        bool atlas=true;
#else
        bool atlas=false;
#endif
        int frames=0;
        float seconds=0;
        while(true)
        {
            Controller.Update();
            var pad=Controller.GetState(0);
            if(pad.B.WasPressed) return 0;
            if(pad.A.WasPressed)
            {
                atlas=!atlas;
                DebugOverlay.WriteLine(atlas?"ATLAS 9 DRAWS":"PAGES 144 DRAWS");
            }
            float dt=clock.Tick();
            seconds+=dt;
            scene.Present(atlas);
            frames++;
            if(frames==120)
            {
                var totals=CacheTrace.Run();
                DebugOverlay.WriteLine("FRAMES 120");
                DebugOverlay.WriteLine(atlas?"ATLAS: 9 DRAW SUBMITS":"PAGES: 144 DRAW SUBMITS");
                DebugOverlay.WriteLine("PAGE CACHE MISSES "+totals.Pages);
                DebugOverlay.WriteLine("ATLAS CACHE MISSES "+totals.Atlases);
                DebugOverlay.WriteLine("PAGES REQUESTED BYTES "+(totals.Pages*4096));
                DebugOverlay.WriteLine("ATLAS REQUESTED BYTES "+(totals.Atlases*65536));
                DebugOverlay.WriteLine("ELAPSED MS "+(int)(seconds*1000));
                frames=0;seconds=0;
            }
        }
    }
}
