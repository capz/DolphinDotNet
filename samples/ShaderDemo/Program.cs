using Dolphin.Input;
using Dolphin.Timing;

internal static class Program
{
    private static int Main()
    {
        using var scene=new ShaderScene();
        var clock=new FrameClock { MaximumDeltaSeconds=0.05f };
        bool paused=false;
        float phase=0;
        while(true)
        {
            Controller.Update();var pad=Controller.GetState(0);
            if(pad.B.WasPressed)return 0;
            if(pad.Start.WasPressed)paused=!paused;
            float dt=clock.Tick();
            if(pad.Y.WasPressed){scene.LowOffset=scene.HighOffset=scene.ColorOffset=0;scene.UseGraph=true;}
            if(pad.A.WasPressed)scene.UseGraph=!scene.UseGraph;
            scene.LowOffset+=pad.Stick.X*dt*35;scene.HighOffset+=pad.Stick.Y*dt*35;
            scene.ColorOffset+=pad.CStick.X*dt*50;
            if(!paused){phase+=dt*0.2f;if(phase>=1)phase-=1;}
            scene.Present(phase<0.5f?phase*2:2-phase*2,paused);
        }
    }
}
