using Dolphin.Audio;
using Dolphin.Graphics;
using Dolphin.Input;
using Dolphin.Timing;

internal static class Program
{
    private static int Main()
    {
        var game=new FoxGame();
        var input=new SampleInput();
        using var record=new SampleRecord("DFOX");var clock=new FrameClock { MaximumDeltaSeconds=0.05f };
        Audio.Initialize();
        using var sounds=new FoxSounds();using var renderer=new FoxRenderer(GraphicsDevice.Default);
        while(true)
        {
            Controller.Update();var pad=input.Read((int)game.State);float dt=clock.Tick();
            if(game.State==FoxState.Title && pad.X.WasPressed && (input.Primary.Button!=ControllerButton.X || pad.Start.IsHeld))return 0;
            record.Update(dt);game.LoadBest(record.Best);
            if(game.State==FoxState.Title && pad.Start.IsHeld && pad.Y.WasPressed)
            {input.CapturePrimary();Dolphin.Diagnostics.DebugOverlay.WriteLine("RELEASE THEN A X Y TO BIND FIRE; B CANCEL");pad=input.Read((int)game.State);}
            if(pad.IsConnected)
            {
                if(pad.B.WasPressed)game.ReturnToTitle();
                else if(game.State==FoxState.Title || game.State==FoxState.GameOver || game.State==FoxState.Victory)
                { if(input.Primary.GetState(pad).WasPressed)game.Start(); }
                else if(pad.Start.WasPressed)game.TogglePause();
                float horizontal=pad.DPadLeft.IsHeld?-1:pad.DPadRight.IsHeld?1:pad.Stick.X;
                float vertical=pad.DPadUp.IsHeld?1:pad.DPadDown.IsHeld?-1:pad.Stick.Y;
                int roll=pad.L.WasPressed?-1:pad.R.WasPressed?1:0;
                pad=input.Read((int)game.State);
                sounds.Play(game.Update(dt,horizontal,vertical,input.Primary.GetState(pad).IsHeld,pad.X.IsHeld,pad.Y.IsHeld,roll));
            }
            if(game.State!=FoxState.Playing)record.Observe(game.HighScore);
            renderer.Present(game,pad.IsConnected);
        }
    }
}
