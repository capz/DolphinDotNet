using Dolphin.Audio;
using Dolphin.Graphics;
using Dolphin.Input;
using Dolphin.Timing;

internal static class Program
{
    private static int Main()
    {
        var game=new FoxGame();var clock=new FrameClock { MaximumDeltaSeconds=0.05f };
        Audio.Initialize();
        using var sounds=new FoxSounds();using var renderer=new FoxRenderer(GraphicsDevice.Default);
        while(true)
        {
            Controller.Update();var pad=Controller.GetState(0);float dt=clock.Tick();
            if(game.State==FoxState.Title && pad.X.WasPressed)return 0;
            if(pad.IsConnected)
            {
                if(pad.B.WasPressed)game.ReturnToTitle();
                else if(game.State==FoxState.Title || game.State==FoxState.GameOver || game.State==FoxState.Victory)
                { if(pad.A.WasPressed)game.Start(); }
                else if(pad.Start.WasPressed)game.TogglePause();
                float horizontal=pad.DPadLeft.IsHeld?-1:pad.DPadRight.IsHeld?1:pad.Stick.X;
                float vertical=pad.DPadUp.IsHeld?1:pad.DPadDown.IsHeld?-1:pad.Stick.Y;
                int roll=pad.L.WasPressed?-1:pad.R.WasPressed?1:0;
                sounds.Play(game.Update(dt,horizontal,vertical,pad.A.IsHeld,pad.X.IsHeld,pad.Y.IsHeld,roll));
            }
            renderer.Present(game,pad.IsConnected);
        }
    }
}
