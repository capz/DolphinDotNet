using Dolphin.Audio;
using Dolphin.Graphics;
using Dolphin.Input;
using Dolphin.Timing;

internal static class Program
{
    private static float Direction(ControllerState controller)
    {
        if(controller.DPadUp.IsHeld)return -1f;
        if(controller.DPadDown.IsHeld)return 1f;
        return -controller.Stick.Y;
    }

    private static int Main()
    {
        // All resources, including samples, are generated or created on the console.
        // Player 1 uses port 0; player 2 uses port 1. No SD/DVD setup.
        var game=new PongGame();
        var clock=new FrameClock { MaximumDeltaSeconds=0.04f };
        Audio.Initialize();
        using var sounds=new PongSounds();
        using var renderer=new PongRenderer(GraphicsDevice.Default);
        bool paused=false;

        while(true)
        {
            Controller.Update();
            var p1=Controller.GetState(0);
            var p2=Controller.GetState(1);
            if(p1.X.WasPressed)return 0; // Explicit way to leave a headless example.

            if(p1.B.WasPressed || p2.B.WasPressed)
            {
                game.Reset();
                paused=false;
            }
            else if(p1.Start.WasPressed || p2.Start.WasPressed)
                paused=!paused;

            float dt=clock.Tick();
            if(p1.IsConnected && p2.IsConnected && !paused)
            {
                if(p1.A.WasPressed || p2.A.WasPressed)
                {
                    if(game.Winner!=0)game.Reset();
                    game.Serve();
                }
                sounds.Play(game.Update(dt,Direction(p1),Direction(p2)));
            }
            // Losing either controller freezes the simulation until both return.
            renderer.Present(game,p1.IsConnected,p2.IsConnected,paused);
        }
    }
}
