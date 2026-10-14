using System;
using System.Threading.Tasks;
using Dolphin.Input;
internal static class Program
{
    private static async Task<int> Main()
    {
        if (Controller.GetState().IsConnected) return 1;
        var controls=new PlayerControls();
        Controller.Update();
        var first=Controller.GetState();
        if (!first.IsConnected || !first.WasConnected || !first.SupportsRumble || !first.A.IsHeld || first.A.WasPressed) return 2;
        if (first.Stick.X!=1 || first.CStick.Y!=1 || first.LeftTrigger!=1) return 3;
        Controller.Update();
        var second=Controller.GetState();
        if (!second.B.WasPressed || !second.A.WasReleased || !second.L.IsHeld || second.WasConnected) return 4;
        if (second.Stick.X!=0 || second.LeftTrigger!=0.5f || !first.A.IsHeld || first.B.IsHeld) return 5;
        for (int i=0;i<1000;i++) if (!Controller.GetState().B.WasPressed) return 6;
        if (!Controller.Rumble(0,0.1f) || Controller.Rumble(1,0.1f)) return 7;
        Controller.Update();
        var gone=Controller.GetState();
        if (gone.IsConnected || !gone.WasDisconnected || gone.B.WasReleased || gone.B.IsHeld) return 8;
        Controller.Update();
        var returned=Controller.GetState();
        if (!returned.WasConnected || !returned.X.IsHeld || returned.X.WasPressed || returned.SupportsRumble) return 9;
        Controller.Configure(0,new ControllerOptions { StickDeadZone=0, CStickDeadZone=0, StickMaximum=100,
            CStickMaximum=100, TriggerMinimum=0, TriggerMaximum=200 });
        Controller.Update();
        var actions=controls.GetState();
        if (!actions.Jump.WasPressed) return 101;
        if (!(actions.Move.X>0.3999f && actions.Move.X<0.4001f)) return 102;
        if (actions.Controller.LeftTrigger!=0.5f) return 103;
        controls.Bindings.Jump=ControllerButton.Y;
        if (!controls.GetState().Jump.WasPressed || !actions.Jump.WasPressed) return 11;
        try { Controller.GetState(4); return 12; } catch (ArgumentOutOfRangeException) { }
        try { Controller.Rumble(0,float.NaN); return 13; } catch (ArgumentOutOfRangeException) { }
        try { Controller.Configure(0,new ControllerOptions { StickDeadZone=1 }); return 14; } catch (ArgumentOutOfRangeException) { }
        try { var invalid=returned[(ControllerButton)0x300]; return 15; } catch (ArgumentOutOfRangeException) { }
        var rejected=await Task.Run(()=> {
            try { Controller.Update(); return false; } catch (InvalidOperationException) { return true; }
        });
        if (!rejected || !Controller.GetState().A.WasPressed) return 16;
        if (!Controller.Rumble(0,1)) return 17; // Entry-point cleanup must stop this.
        return 0;
    }
}
