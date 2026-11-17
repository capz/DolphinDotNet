using Dolphin.Graphics;
using Dolphin.Diagnostics;
internal static class Program
{
    private static void Main()
    {
var device=GraphicsDevice.Default;
device.ConfigureDisplay(new DisplayOptions{Resolution=GraphicsResolution.R640x480,AntialiasingMode=AntialiasingMode.FullResolution});
if(device.Width!=640||device.Height!=480||device.FramePartCount!=2)throw new System.Exception("Full AA dimensions");
using var ui=new Draw2D(device,new Draw2DOptions{Width=640,Height=480,ScaleMode=CanvasScaleMode.Stretch,VertexCapacity=8192});
using var capture=new SceneCapture(device,320,240,NativeTextureFormat.Rgb565);
DebugOverlay.TextVisible=false;DebugOverlay.PerformanceVisible=false;
while(true){
    device.BeginFrame(Color32.Black);
    do{
        ui.Begin();
        // Fine horizontal stripes prove the framebuffer retains vertical detail.
        for(int y=0;y<480;y++)ui.FillRectangle(new RectF(0,y,160,1),y%4<2?Color32.White:Color32.Black);
        // A long diagonal and crosshair expose projection or resolve seams.
        ui.FillTriangle(new Vector2(190,10),new Vector2(460,470),new Vector2(450,470),new Color32(255,80,40));
        ui.FillRectangle(new RectF(170,238,300,4),new Color32(40,220,100));
        ui.End();capture.Capture(new Viewport(0,0,640,480));
        ui.Begin();ui.DrawTexture(capture.Texture,new RectF(480,0,160,480),Color32.White);ui.End();
    }while(device.NextFramePart());
    device.EndFrame();
}

    }
}
