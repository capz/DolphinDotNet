using Dolphin.Audio;
using Dolphin.Graphics;
using Dolphin.Input;
using Dolphin.Timing;

internal static class Program
{
    private static int Main()
    {
        var game=new GreenMenGame();
        var clock=new FrameClock { MaximumDeltaSeconds=0.05f };
        Audio.Initialize();
        using var sounds=new GreenMenSounds();
        using var renderer=new GreenMenRenderer(GraphicsDevice.Default);
        while(true)
        {
            Controller.Update();
            var pad=Controller.GetState(0);
            float dt=clock.Tick(); // Tick even while paused/disconnected; never accumulate a catch-up.
            if(pad.X.WasPressed)return 0;
            if(pad.IsConnected)
            {
                if(pad.B.WasPressed)game.ReturnToTitle();
                else if(game.State==GreenMenState.Title || game.State==GreenMenState.GameOver)
                {
                    if(pad.A.WasPressed)game.Start();
                }
                else if(pad.Start.WasPressed)game.TogglePause();
                float movement=pad.DPadLeft.IsHeld?-1:pad.DPadRight.IsHeld?1:pad.Stick.X;
                sounds.Play(game.Update(dt,movement,pad.A.IsHeld));
            }
            renderer.Present(game,pad.IsConnected);
        }
    }
}
