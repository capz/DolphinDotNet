using Dolphin;
using Dolphin.Hardware;
using Dolphin.Input;
using Dolphin.Graphics;
using Dolphin.Diagnostics;

internal static class Program
{
    private static void Print(HardwareSnapshot found)
    {
        DebugOverlay.WriteLine("HARDWARE INVENTORY");
        DebugOverlay.WriteLine("Status: 0 absent 1 present 2 busy 3 unknown 4 unsupported");
        foreach(var device in found.Devices)
        {
            // Integer names avoid dependence on unsupported Enum.ToString overloads.
            string label=((int)device.Kind).ToString();
            string slot=((int)device.Port).ToString();
            string status=((int)device.Presence).ToString();
            DebugOverlay.WriteLine(string.Concat(string.Concat(label," / "),string.Concat(slot,string.Concat(" = ",status))));
        }
        DebugOverlay.WriteLine("A: EXTENDED SCAN   START+X: RESTART");
        DebugOverlay.WriteLine("START+Y: SYSTEM MENU   RESET BUTTON: STATUS");
    }
    private static int Main()
    {
        Print(Hardware.Refresh());
        var device=GraphicsDevice.Default;
        bool resetWasDown=false;
        while(true)
        {
            Controller.Update();
            bool resetIsDown=Platform.ResetButtonPressed;
            if(resetIsDown && !resetWasDown) DebugOverlay.WriteLine("PHYSICAL RESET BUTTON PRESSED");
            resetWasDown=resetIsDown;
            var pad=Controller.GetState(0);
            if(pad.IsConnected && pad.Start.IsHeld && pad.X.WasPressed)
                Platform.Reboot(RebootMode.Restart);
            if(pad.IsConnected && pad.Start.IsHeld && pad.Y.WasPressed)
                Platform.Reboot(RebootMode.SystemMenu);
            if(pad.IsConnected && pad.A.WasPressed)
                Print(Hardware.Refresh(HardwareProbeMode.Extended));
            device.BeginFrame(Color32.Black);
            device.EndFrame();
        }
    }
}
