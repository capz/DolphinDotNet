using Dolphin;
using Dolphin.Hardware;
using Dolphin.Input;
using Dolphin.Graphics;
using Dolphin.Diagnostics;
using Dolphin.Timing;

internal static class Program
{
    private static string Kind(HardwareKind kind)
    {
        switch(kind)
        {
            case HardwareKind.Controller:return "CONTROLLER";case HardwareKind.MemoryCard:return "MEMORY CARD";
            case HardwareKind.BroadbandAdapter:return "BBA";case HardwareKind.GameDisc:return "GAME DISC";
            case HardwareKind.GcLoader:return "GC LOADER";case HardwareKind.SdCard:return "SD ADAPTER";
            case HardwareKind.Sd2Sp2:return "SD2SP2";case HardwareKind.GameBoyAdvanceLink:return "GBA LINK";
            case HardwareKind.GameBoyPlayer:return "GAME BOY PLAYER";default:return "MODEM";
        }
    }
    private static string Presence(HardwarePresence state)
    {return state==HardwarePresence.Present?"PRESENT":state==HardwarePresence.Absent?"ABSENT":state==HardwarePresence.Busy?"BUSY":state==HardwarePresence.Unsupported?"UNSUPPORTED":"UNKNOWN";}
    private static string[] Labels(HardwareSnapshot found)
    {
        var rows=new string[found.Devices.Length];
        for(int i=0;i<rows.Length;i++)
        {var d=found.Devices[i];rows[i]=$"{Kind(d.Kind),-17} PORT {(int)d.Port:D2} {Presence(d.Presence)}";}
        return rows;
    }
    private static int Main()
    {
        var found=Hardware.Refresh();var rows=Labels(found);string cards="";var clock=new FrameClock();float poll=0;
        using var draw=new Draw2D(GraphicsDevice.Default,new Draw2DOptions {Width=640,Height=480});
        using var text=new SampleText();bool resetWasDown=false;
        while(true)
        {
            Controller.Update();var pad=Controller.GetState();
            for(int i=0;i<4;i++)
            {
                var state=Controller.GetState(i);
                if(state.WasConnected)DebugOverlay.WriteLine($"CONTROLLER {i+1} CONNECTED");
                if(state.WasDisconnected)DebugOverlay.WriteLine($"CONTROLLER {i+1} DISCONNECTED");
            }
            poll-=clock.Tick();if(poll<=0){MemoryCards.Update();poll=2;cards=$"CARD A {Presence(MemoryCards.GetState(MemoryCardSlot.A).Presence)}   CARD B {Presence(MemoryCards.GetState(MemoryCardSlot.B).Presence)}";}
            bool reset=Platform.ResetButtonPressed;
            if(reset&&!resetWasDown)DebugOverlay.WriteLine("PHYSICAL RESET BUTTON PRESSED");resetWasDown=reset;
            if(pad.IsConnected&&pad.Start.IsHeld&&pad.X.WasPressed)Platform.Reboot(RebootMode.Restart);
            if(pad.IsConnected&&pad.Start.IsHeld&&pad.Y.WasPressed)Platform.Reboot(RebootMode.SystemMenu);
            if(pad.A.WasPressed){found=Hardware.Refresh(HardwareProbeMode.Extended);rows=Labels(found);}
            var device=GraphicsDevice.Default;device.BeginFrame(Color32.Black);draw.Begin();
            text.Draw(draw,"HARDWARE INVENTORY",24,130,4,Color32.White);
            for(int i=0;i<rows.Length&&i<12;i++)text.Draw(draw,rows[i],24,170+i*17,2,Color32.White);
            text.Draw(draw,cards,24,395,2,new Color32(110,230,230));
            text.Draw(draw,"A EXTENDED SCAN  START X REBOOT  START Y MENU  Z STATS",24,440,2,Color32.White);
            draw.End();device.EndFrame();
        }
    }
}
