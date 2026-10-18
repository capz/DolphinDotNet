using Dolphin.Graphics;
using Dolphin.Input;
using Dolphin.Timing;

internal static class Program
{
    private static int Main()
    {
        var game=new KartGame();var clock=new FrameClock { MaximumDeltaSeconds=.05f };
        using var network=new KartNetwork(unchecked((int)Clock.Timestamp));
        using var renderer=new KartRenderer(GraphicsDevice.Default);
        bool paired=false;
        while(true)
        {
            Controller.Update();var pad=Controller.GetState(0);float dt=clock.Tick();
            if(pad.X.WasPressed && pad.Start.IsHeld)return 0;
            if(pad.Y.WasPressed)game=new KartGame();
            if(pad.B.WasPressed && pad.Start.IsHeld)game.Rescue();
            float steer=pad.DPadLeft.IsHeld?-1:pad.DPadRight.IsHeld?1:pad.Stick.X;
            game.Update(dt,steer,pad.IsConnected && pad.A.IsHeld,pad.B.IsHeld,
                pad.IsConnected && pad.R.IsHeld,pad.IsConnected && pad.X.IsHeld);
            if(pad.L.WasPressed)game.Items.Use(game);
            network.Update(dt,game);
            if(network.Peer.Paired && !paired)game=new KartGame { Countdown=3,X=KartGame.Radius+(network.Peer.InsideLane?-2:2) };
            paired=network.Peer.Paired;
            renderer.Present(game,network,pad.IsConnected);
        }
    }
}
