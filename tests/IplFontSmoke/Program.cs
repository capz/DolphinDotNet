using System;
using Dolphin.Graphics;
using Dolphin.System;
using Dolphin.Input;
using Dolphin.Diagnostics;
using System.Threading.Tasks;

internal static class Program
{
    private static int Main()
    {
        var device=GraphicsDevice.Default;using var font=IplFont.Load();
        if(font.NativeBytes!=512*256*2||font.CellWidth<1||font.CellHeight<1||font.LineHeight<1)return 1;
        var narrow=font.MeasureText("iiii");var wide=font.MeasureText("WWWW");if(wide.X<=0||narrow.X!=font.MeasureText("i").X*4)return 2;
        if(font.MeasureText("A\nB").Y!=font.CellHeight+font.LineHeight)return 3;
        try{font.MeasureText(null!);return 4;}catch(ArgumentNullException){}
        try{font.MeasureText("A",0);return 5;}catch(ArgumentOutOfRangeException){}
        using(var second=IplFont.Load()){if(second.SharedFirmwareBytes!=font.SharedFirmwareBytes)return 6;second.Dispose();try{second.MeasureText("A");return 7;}catch(ObjectDisposedException){}}
        using(var facade=SystemFont.Load())
        {
            if(facade.CellWidth!=font.CellWidth||facade.LineHeight!=font.LineHeight||facade.MeasureText("A").X!=font.MeasureText("A").X)return 10;
        }
        int before=device.NativeBytes;device.BeginFrame(Color32.Black);
        try{IplFont.Load();return 8;}catch(InvalidOperationException){}
        device.EndFrame();if(device.NativeBytes!=before)return 9;
        using var ui=new Draw2D(device,new Draw2DOptions{Width=640,Height=480});
        DebugOverlay.WriteLine("IPL FONT READY");
        while(true){Controller.Update();if(Controller.GetState(0).Start.WasPressed)return 0;device.BeginFrame(new Color32(245,239,217));ui.Begin();ui.DrawText(font,"Native IPL firmware font",new Vector2(32,60),new Color32(35,75,55));ui.DrawText(font,"Proportional: iii WWW 123\nBackpack / Welcome home!",new Vector2(32,125),Color32.Black,.75f);ui.End();device.EndFrame();Task.Delay(1).GetAwaiter().GetResult();}
    }
}
