using System;
using Dolphin.Input;

internal class Screen : InputContext
{
    public int Calls, Activations, Deactivations, Disconnects;
    public ControllerState Last;
    public ControllerButton ConsumeButtons;
    public bool ConsumeAnalog, PopOnStart, RejectReentry, RejectLifecycleMutation;
    public bool ReentryRejected, LifecycleMutationRejected;
    public PlayerInputState Actions;
    private readonly PlayerControls controls=new PlayerControls();
    public InputContext? PushOnStart;
    public Screen(bool modal=true) : base(modal) { }
    public override void OnActivated()
    {
        Activations++;
        if (RejectLifecycleMutation)
        {
            try { Router.Pop(); }
            catch (InvalidOperationException) { LifecycleMutationRejected=true; }
        }
    }
    public override void OnDeactivated() { Deactivations++; }
    public override void OnDisconnected() { Disconnects++; }
    public virtual void HandleInput(ControllerState unrelatedOverload) { Calls+=1000; }
    public override void HandleInput(InputFrame input)
    {
        Calls++;
        Last=input.State;
        Actions=controls.GetState(input);
        if (RejectReentry)
        {
            try { Router.Update(input.State); }
            catch (InvalidOperationException) { ReentryRejected=true; }
        }
        input.Consume(ConsumeButtons);
        if (ConsumeAnalog)
        {
            input.ConsumeStick(ControllerStick.Stick);
            input.ConsumeLeftTrigger();
        }
        if (Last.Start.WasPressed)
        {
            if (PushOnStart!=null) Router.Push(PushOnStart);
            else if (PopOnStart) Router.Pop();
        }
    }
}

internal static class Program
{
    private static ControllerState Pad(ControllerButton held=ControllerButton.None,
        ControllerButton pressed=ControllerButton.None, ControllerButton released=ControllerButton.None, int flags=1) =>
        new ControllerState(flags, (int)held, (int)pressed, (int)released,
            new StickState(0.5f,0.25f), new StickState(-0.5f,0.75f), 0.6f, 0.8f);
    public static int Main()
    {
        var router=new InputRouter(capacity:4);
        var game=new Screen();
        var menu=new Screen { PopOnStart=true };
        router.Push(game);
        router.Update(Pad());
        game.PushOnStart=menu;
        router.Update(Pad(ControllerButton.Start,ControllerButton.Start));
        if (game.Calls!=2 || menu.Calls!=0 || game.Deactivations!=1 || menu.Activations!=1) return 1;
        router.Update(Pad(ControllerButton.Start));
        if (menu.Last.Start.IsHeld || menu.Last.Start.WasPressed) return 2;
        router.Update(Pad(released:ControllerButton.Start));
        if (menu.Last.Start.WasReleased) return 3;
        var oldMenu=menu.Last;
        router.Update(Pad(ControllerButton.Start,ControllerButton.Start));
        if (router.Current!=game || game.Calls!=2 || game.Activations!=2 || menu.Deactivations!=1) return 4;
        router.Update(Pad(ControllerButton.Start));
        if (game.Last.Start.IsHeld || oldMenu.Start.IsHeld) return 5;
        router.Update(Pad(released:ControllerButton.Start));
        game.PushOnStart=null;
        router.Update(Pad(ControllerButton.A,ControllerButton.A));
        if (!game.Last.A.WasPressed) return 6;

        // Non-modal handlers consume selected physical input, leaving the rest intact.
        var overlay=new Screen(false) { ConsumeButtons=ControllerButton.A, ConsumeAnalog=true };
        router.Push(overlay, suppressHeldButtons:false);
        var raw=Pad(ControllerButton.A|ControllerButton.B,ControllerButton.A|ControllerButton.B);
        router.Update(raw);
        if (!overlay.Last.A.WasPressed || game.Last.A.IsHeld || !game.Last.B.WasPressed || game.Actions.Jump.IsHeld || !game.Actions.Run.IsHeld) return 7;
        if (game.Last.Stick.X!=0 || game.Last.LeftTrigger!=0 || game.Last.CStick.X!=-0.5f || game.Last.RightTrigger!=0.8f) return 8;
        if (!raw.A.WasPressed || raw.Stick.X!=0.5f || game.Deactivations!=1) return 9;
        router.Update(Pad(flags:4));
        if (game.Disconnects!=1 || overlay.Disconnects!=1) return 10;
        router.Pop();
        router.Update(Pad(ControllerButton.B,flags:3));
        if (game.Last.B.IsHeld) return 11;
        router.Update(Pad(released:ControllerButton.B));
        if (game.Last.B.WasReleased) return 12;
        router.Update(Pad(ControllerButton.B,ControllerButton.B));
        if (!game.Last.B.WasPressed) return 13;

        // Capturing waits for all physical buttons to be released and a fresh press.
        var attack=new ButtonBinding(ControllerButton.A);
        var capture=new ButtonCaptureContext(attack);
        router.Push(capture);
        router.Update(Pad(ControllerButton.B));
        if (capture.Status!=ButtonCaptureStatus.Waiting) return 14;
        router.Update(Pad(released:ControllerButton.B));
        int calls=game.Calls;
        router.Update(Pad(ControllerButton.X,ControllerButton.X));
        if (capture.Status!=ButtonCaptureStatus.Completed || attack.Button!=ControllerButton.X || router.Current!=game) return 15;
        if (game.Calls!=calls) return 16;
        router.Update(Pad(ControllerButton.X));
        if (attack.GetState(game.Last).IsHeld) return 17;
        router.Update(Pad(released:ControllerButton.X));
        router.Update(Pad(ControllerButton.X,ControllerButton.X));
        if (!attack.GetState(game.Last).WasPressed) return 18;

        capture=new ButtonCaptureContext(attack);
        router.Push(capture);
        router.Update(Pad());
        router.Update(Pad(ControllerButton.B,ControllerButton.B));
        if (capture.Status!=ButtonCaptureStatus.Cancelled || attack.Button!=ControllerButton.X) return 19;
        capture=new ButtonCaptureContext(attack);
        router.Push(capture);
        router.Update(Pad());
        router.Update(Pad(flags:4));
        if (capture.Status!=ButtonCaptureStatus.Cancelled || router.Current!=game) return 20;
        capture=new ButtonCaptureContext(attack, ControllerButton.None, ControllerButton.B|ControllerButton.Y);
        router.Push(capture);
        router.Update(Pad());
        router.Update(Pad(ControllerButton.A,ControllerButton.A));
        if (capture.Status!=ButtonCaptureStatus.Waiting) return 21;
        router.Update(Pad());
        router.Update(Pad(ControllerButton.B|ControllerButton.Y,ControllerButton.B|ControllerButton.Y));
        if (attack.Button!=ControllerButton.B || capture.CapturedButton!=ControllerButton.B) return 22;

        // A modal blocks lower contexts; clearing does not briefly resume them.
        router.Update(Pad());
        var modal=new Screen();
        router.Push(modal);
        int activated=game.Activations;
        router.Clear();
        if (router.Count!=0 || game.Activations!=activated || modal.Deactivations!=1) return 23;
        if (router.Pop()!=null) return 24;
        router.Push(game);
        try { router.Push(game); return 25; } catch (InvalidOperationException) { }
        try { new InputRouter().Push(game); return 26; } catch (InvalidOperationException) { }
        try { new ButtonBinding(ControllerButton.All); return 27; } catch (ArgumentOutOfRangeException) { }
        try { new ButtonCaptureContext(attack, allowedButtons:ControllerButton.None); return 28; } catch (ArgumentOutOfRangeException) { }
        try { new InputRouter(capacity:0); return 29; } catch (ArgumentOutOfRangeException) { }
        var guarded=new Screen { RejectReentry=true, RejectLifecycleMutation=true };
        router.Push(guarded);
        router.Update(Pad());
        if (!guarded.ReentryRejected || !guarded.LifecycleMutationRejected) return 30;
        var full=new InputRouter(capacity:1);
        full.Push(new Screen());
        try { full.Push(new Screen()); return 31; } catch (InvalidOperationException) { }
        router.Clear();
        router.Push(game);
        router.Update(Pad());
        capture=new ButtonCaptureContext(attack);
        router.Push(capture);
        var passThrough=new Screen(false);
        router.Push(passThrough);
        router.Update(Pad());
        router.Update(Pad(ControllerButton.Y,ControllerButton.Y));
        if (capture.Status!=ButtonCaptureStatus.Completed || attack.Button!=ControllerButton.Y || router.Current!=passThrough) return 32;
        if (router.Count!=2 || router.Remove(capture)) return 33;
        router.Clear();
        return 0;
    }
}
