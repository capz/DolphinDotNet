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
        var input1=new SampleInput(0);var input2=new SampleInput(1);
        using var leftRecord=new SampleRecord("DPNG",false,"LeftStreak");
        using var rightRecord=new SampleRecord("DPNG",false,"RightStreak");
        int previousWinner=0,leftStreak=0,rightStreak=0;

        while(true)
        {
            Controller.Update();
            var p1=input1.Read(paused?1:game.Winner);
            var p2=input2.Read(paused?1:game.Winner);
            if(p1.X.WasPressed)return 0; // Explicit way to leave a headless example.

            if(p1.B.WasPressed || p2.B.WasPressed)
            {
                game.Reset();
                paused=false;input1.Suppress();input2.Suppress();
            }
            else if(p1.Start.WasPressed || p2.Start.WasPressed)
                paused=!paused;

            float dt=clock.Tick();
            leftRecord.Update(dt);rightRecord.Update(dt);
            p1=input1.Read(paused?1:game.Winner);p2=input2.Read(paused?1:game.Winner);
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
            if(game.Winner!=0 && previousWinner==0)
            {
                if(game.Winner==1){leftStreak++;rightStreak=0;leftRecord.Observe(leftStreak);}
                else{rightStreak++;leftStreak=0;rightRecord.Observe(rightStreak);}
                SampleFeedback.Pulse(.25f,game.Winner-1);
            }
            previousWinner=game.Winner;
            renderer.Present(game,p1.IsConnected,p2.IsConnected,paused,leftRecord.Best,rightRecord.Best);
        }
    }
}
