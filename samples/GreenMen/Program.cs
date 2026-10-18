using Dolphin.Audio;
using Dolphin.Graphics;
using Dolphin.Input;
using Dolphin.Timing;

internal static class Program
{
    private static int Main()
    {
        var game=new GreenMenGame();
        var input=new SampleInput();
        using var record=new SampleRecord("DGRN");
        var clock=new FrameClock { MaximumDeltaSeconds=0.05f };
        Audio.Initialize();
        using var sounds=new GreenMenSounds();
        using var renderer=new GreenMenRenderer(GraphicsDevice.Default);
        while(true)
        {
            Controller.Update();
            var pad=input.Read((int)game.State);
            float dt=clock.Tick(); // Tick even while paused/disconnected; never accumulate a catch-up.
            if(pad.X.WasPressed && (input.Primary.Button!=ControllerButton.X || pad.Start.IsHeld))return 0;
            record.Update(dt);game.LoadBest(record.Best);
            if(game.State==GreenMenState.Title && pad.Start.IsHeld && pad.Y.WasPressed)
            {input.CapturePrimary();Dolphin.Diagnostics.DebugOverlay.WriteLine("RELEASE THEN A X Y TO BIND FIRE; B CANCEL");pad=input.Read((int)game.State);}
            if(pad.IsConnected)
            {
                if(pad.B.WasPressed)game.ReturnToTitle();
                else if(game.State==GreenMenState.Title || game.State==GreenMenState.GameOver)
                {
                    if(input.Primary.GetState(pad).WasPressed)game.Start();
                }
                else if(pad.Start.WasPressed)game.TogglePause();
                float movement=pad.DPadLeft.IsHeld?-1:pad.DPadRight.IsHeld?1:pad.Stick.X;
                pad=input.Read((int)game.State);
                sounds.Play(game.Update(dt,movement,input.Primary.GetState(pad).IsHeld));
            }
            if(game.State!=GreenMenState.Playing)record.Observe(game.HighScore);
            renderer.Present(game,pad.IsConnected);
        }
    }
}
