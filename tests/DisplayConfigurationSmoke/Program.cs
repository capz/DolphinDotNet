using System;
using Dolphin.Graphics;
class Program
{
    static void Equal(float a,float b){if(float.IsNaN(a)||Math.Abs(a-b)>.002f)throw new Exception("Display value mismatch.");}
    static int Main()
    {
        var d=GraphicsDevice.Default;
        if(d.Display.Widescreen)return 1;
        var camera=new Camera();var explicitCamera=new Camera(60,4f/3f);
        var viewport=new Viewport(0,0,640,480);var point=new Vector3(1,0,0);
        float initial=camera.Project(point,viewport).X;
        using var draw=new Draw2D(d);
        Equal(draw.LogicalSize.X,640);Equal(draw.LogicalSize.Y,480);
        d.ConfigureDisplay(new DisplayOptions{Widescreen=true});
        if(!d.Display.Widescreen||d.Display.Resolution!=GraphicsResolution.R640x480)return 2;
        Equal(d.Display.RenderSize.X,640);Equal(d.Display.RenderSize.Y,480);Equal(d.Display.OutputSize.Y,480);
        Equal(draw.LogicalSize.X,480*16f/9);Equal(draw.LogicalSize.Y,480);
        float wide=camera.Project(point,viewport).X;Equal(wide-320,(initial-320)*.75f);
        Equal(explicitCamera.Project(point,viewport).X,initial);
        var ortho=new Camera();ortho.SetOrthographic(8,6,.1f,100);
        float fixedOrtho=ortho.Project(point,viewport).X;
        ortho.AspectRatioMode=AspectRatioMode.Display;
        Equal(ortho.Project(point,viewport).X-320,(fixedOrtho-320)*.75f);
        var roundTrip=camera.Unproject(camera.Project(point,viewport),viewport);Equal(roundTrip.X,point.X);
        using var fit=new Draw2D(d,new Draw2DOptions{Width=320,Height=240,ScaleMode=CanvasScaleMode.Fit});
        using var stretch=new Draw2D(d,new Draw2DOptions{Width=320,Height=240,ScaleMode=CanvasScaleMode.Stretch});
        using var legacy=new Draw2D(d,new Draw2DOptions{Width=320,Height=240});
        using var geometry=new Geometry2DBuilder(6).AddForDisplayTest();
        d.BeginFrame(Color32.Black);
        try{d.ConfigureDisplay(new DisplayOptions());return 3;}catch(InvalidOperationException){}
        draw.Begin();Equal(draw.LogicalSize.X,480*16f/9);draw.FillRectangle(new RectF(0,0,20,20),Color32.White);draw.ResetClip();draw.DrawGeometry(geometry);draw.End();
        draw.Begin(new Camera2D(),new Viewport(0,0,320,240));Equal(draw.LogicalSize.X,240*16f/9);draw.End();
        fit.Begin();Equal(fit.LogicalSize.X,320);Equal(fit.LogicalSize.Y,240);fit.End();
        stretch.Begin();Equal(stretch.LogicalSize.X,320);stretch.End();legacy.Begin();Equal(legacy.LogicalSize.X,320);legacy.End();
        d.EndFrame();
        try{d.ConfigureDisplay(new DisplayOptions{Resolution=GraphicsResolution.R640x576,Scan=ScanMode.Interlaced});return 7;}catch(OutOfMemoryException){}
        Equal(d.Width,640);Equal(d.Display.OutputSize.Y,480);if(!d.Display.Widescreen)return 8;
        d.ConfigureDisplay(new DisplayOptions{Resolution=GraphicsResolution.R640x576,Scan=ScanMode.Progressive,Widescreen=true});
        Equal(d.Display.OutputSize.Y,576);Equal(d.Display.RenderSize.Y,480);
        d.ConfigureDisplay(new DisplayOptions{Resolution=GraphicsResolution.R320x240,Scan=ScanMode.Progressive});
        Equal(d.Width,320);Equal(d.Height,240);Equal(draw.LogicalSize.X,320);
        try{d.ConfigureDisplay(new DisplayOptions{Resolution=GraphicsResolution.R320x240,Scan=ScanMode.Interlaced});return 4;}catch(InvalidOperationException){}
        try{d.ConfigureDisplay(new DisplayOptions{Resolution=(GraphicsResolution)99});return 5;}catch(ArgumentOutOfRangeException){}
        Equal(d.Width,320);if(d.Display.Widescreen)return 6;
        d.ConfigureDisplay(new DisplayOptions{Resolution=GraphicsResolution.R640x480,Scan=ScanMode.Interlaced});
        Equal(camera.Project(point,viewport).X,initial);
        return 0;
    }
}
static class FixtureGeometry
{
    public static Geometry2D AddForDisplayTest(this Geometry2DBuilder builder){builder.AddRectangle(new RectF(0,0,20,20),Color32.White);return builder.Build();}
}
