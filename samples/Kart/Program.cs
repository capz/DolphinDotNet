using Dolphin.Graphics;
using Dolphin.Input;
using Dolphin.Timing;

internal static class Program
{
    private static int Main()
    {
        var game=new KartGame(unchecked((int)Clock.Timestamp));var clock=new FrameClock { MaximumDeltaSeconds=.05f };
        using var network=new KartNetwork(unchecked((int)Clock.Timestamp));
        using var renderer=new KartRenderer(GraphicsDevice.Default);
        bool paired=false;
        var input=new SampleInput(0,Dolphin.Input.ControllerButton.L);
        var race=new Stopwatch();
        using var record=new SampleRecord("DKRT",true);
        using var audio=new SampleEngine();
        KartItem held=KartItem.None;float previousStun=0;
        while(true)
        {
            Controller.Update();var pad=input.Read();float dt=clock.Tick();
            if(pad.X.WasPressed && pad.Start.IsHeld)return 0;
            if(pad.Y.WasPressed){game=new KartGame(unchecked((int)Clock.Timestamp));race.Reset();}
            if(pad.B.WasPressed && pad.Start.IsHeld)game.Rescue();
            float steer=pad.DPadLeft.IsHeld?-1:pad.DPadRight.IsHeld?1:pad.Stick.X;
            game.Update(dt,steer,pad.IsConnected && pad.A.IsHeld,pad.B.IsHeld,
                pad.IsConnected && pad.R.IsHeld,pad.IsConnected && pad.X.IsHeld);
            if(input.Primary.GetState(pad).WasPressed && game.Items.Use(game)){audio.Cue();SampleFeedback.Pulse(.08f);}
            network.Update(dt,game);
            if(network.Peer.Paired && !paired){game=new KartGame(unchecked((int)Clock.Timestamp)) { Countdown=3,X=KartGame.Radius+(network.Peer.InsideLane?-2:2) };race.Reset();}
            paired=network.Peer.Paired;
            if(game.Countdown<=0&&!game.Finished)race.Start();else race.Stop();
            game.RaceTime=(float)race.ElapsedSeconds;
            if(game.Finished)record.Observe((int)(game.Elapsed*1000));
            record.Update(dt);
            if(game.Stun>0&&previousStun<=0){audio.Cue();SampleFeedback.Pulse(.2f);}
            if(game.Items.Held!=KartItem.None&&held==KartItem.None)audio.Cue();
            held=game.Items.Held;previousStun=game.Stun;
            audio.Update(game.Speed,pad.IsConnected&&!game.Finished,game.Boost>0);
            renderer.Present(game,network,pad.IsConnected,record.Best);
        }
    }
}
