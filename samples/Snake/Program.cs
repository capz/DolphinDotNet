using System;
using Dolphin.Graphics;
using Dolphin.Input;
using Dolphin.Timing;

internal static class Program
{
    private static int InputDirection(ControllerState pad)
    {
        if(pad.DPadUp.IsHeld)return SnakeGame.Up;
        if(pad.DPadRight.IsHeld)return SnakeGame.Right;
        if(pad.DPadDown.IsHeld)return SnakeGame.Down;
        if(pad.DPadLeft.IsHeld)return SnakeGame.Left;
        float x=pad.Stick.X,y=pad.Stick.Y;
        float ax=x<0f?-x:x,ay=y<0f?-y:y;
        if(ax<0.42f && ay<0.42f)return -1;
        return ax>ay?(x>0f?SnakeGame.Right:SnakeGame.Left)
                    :(y>0f?SnakeGame.Up:SnakeGame.Down);
    }

    private static bool TitleSwitch(ControllerState pad)
    {
        return pad.DPadUp.WasPressed || pad.DPadDown.WasPressed ||
               pad.Stick.Y>0.65f || pad.Stick.Y< -0.65f;
    }

    private static int Main()
    {
        using var store=new SnakeSaveStore();
        using var renderer=new SnakeRenderer(GraphicsDevice.Default);
        var game=new SnakeGame();
        var input=new SampleInput();
        var clock=new FrameClock { MaximumDeltaSeconds=0.2f };
        var autosave=new SaveInterval();
        SnakeScreen screen=SnakeScreen.Title;
        int selection=store.HasSave?0:1;
        bool stickLatched=false;

        while(true)
        {
            Controller.Update();
            var pad=input.Read((int)screen);
            float dt=clock.Tick(); // Used only during active, connected play.
            if(store.Update(dt,screen==SnakeScreen.Playing || screen==SnakeScreen.Paused?game:null))
                autosave.Saved();
            if(screen==SnakeScreen.Title)
            {
                if(pad.X.WasPressed)return 0;
                bool stickMoved=pad.Stick.Y>0.65f || pad.Stick.Y< -0.65f;
                if((pad.DPadUp.WasPressed || pad.DPadDown.WasPressed ||
                    (stickMoved && !stickLatched)) && store.HasSave)
                    selection=1-selection;
                stickLatched=stickMoved;
                if(!store.HasSave)selection=1;

                if(pad.A.WasPressed)
                {
                    if(selection==0 && store.TryLoad(out var resumed) && resumed!=null)
                    {
                        game=resumed;autosave=new SaveInterval();
                        screen=SnakeScreen.Playing;
                    }
                    else if(selection==1)
                    {
                        store.Clear();
                        uint seed=(uint)Clock.Timestamp ^ 0xA15EC3B7u;
                        game=new SnakeGame(seed);
                        autosave=new SaveInterval();
                        store.Save(game); // New Game immediately replaces stale progress.
                        screen=SnakeScreen.Playing;
                    }
                }
            }
            else if(screen==SnakeScreen.Playing)
            {
                if(pad.B.WasPressed)
                {
                    store.Save(game);
                    autosave.Saved();
                    selection=store.HasSave?0:1;
                    screen=SnakeScreen.Title;
                }
                else if(pad.Start.WasPressed)screen=SnakeScreen.Paused;
                else if(pad.IsConnected)
                {
                    int turn=InputDirection(pad);
                    if(turn>=0)game.Turn(turn);
                    SnakeMove result=game.Advance(dt);
                    if(result==SnakeMove.Died || result==SnakeMove.Won)
                    {
                        SampleFeedback.Pulse(.2f);
                        store.Clear(); // A finished run is not resumable.
                        selection=1;
                        screen=SnakeScreen.GameOver;
                    }
                    else if(autosave.Advance(dt))
                    {
                        if(store.Save(game))autosave.Saved();
                        else autosave.Failed();
                    }
                }
            }
            else if(screen==SnakeScreen.Paused)
            {
                if(pad.B.WasPressed)
                {
                    store.Save(game);
                    autosave.Saved();
                    selection=store.HasSave?0:1;
                    screen=SnakeScreen.Title;
                }
                else if(pad.Start.WasPressed || pad.A.WasPressed)
                    screen=SnakeScreen.Playing;
            }
            else if(screen==SnakeScreen.GameOver)
            {
                if(pad.B.WasPressed)
                {
                    selection=store.HasSave?0:1;
                    screen=SnakeScreen.Title;
                }
                else if(pad.A.WasPressed)
                {
                    store.Clear();
                    uint seed=(uint)Clock.Timestamp ^ 0xA15EC3B7u;
                    game=new SnakeGame(seed);
                    autosave=new SaveInterval();
                    store.Save(game);
                    screen=SnakeScreen.Playing;
                }
            }

            renderer.Present(game,screen,selection,store.HasSave,
                string.Concat(string.Concat(store.CardLabel,"  "),store.Message),pad.IsConnected);
        }
    }
}
