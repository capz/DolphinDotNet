using System;
namespace Dolphin.Graphics;
public enum GraphicsResolution { Auto=0,R320x240=1,R640x480=2,R640x576=3 }
public enum ScanMode { Auto=0,Interlaced=1,Progressive=2 }
public enum AspectRatioMode { Display=0,Explicit=1 }
public enum CanvasScaleMode { Display=0,Fit=1,Stretch=2 }
public sealed class DisplayOptions
{
    public GraphicsResolution Resolution {get;set;}=GraphicsResolution.Auto;
    public ScanMode Scan {get;set;}=ScanMode.Auto;
    /// <summary>Force anamorphic 16:9 rendering. Does not change video output.</summary>
    public bool Widescreen {get;set;}=false;
}
/// <summary>Live, read-only information about the resolved native display.</summary>
public sealed class DisplayInfo
{
    internal DisplayInfo(){}
    public GraphicsResolution Resolution=>(GraphicsResolution)NativeGraphics.DeviceDisplayStat(0);
    public ScanMode Scan=>(ScanMode)NativeGraphics.DeviceDisplayStat(1);
    public bool Widescreen=>NativeGraphics.DeviceDisplayStat(7)!=0;
    public float AspectRatio=>Widescreen?16f/9f:4f/3f;
    public Vector2 OutputSize=>new Vector2(NativeGraphics.DeviceDisplayStat(2),NativeGraphics.DeviceDisplayStat(3));
    public Vector2 RenderSize=>new Vector2(NativeGraphics.DeviceDisplayStat(4),NativeGraphics.DeviceDisplayStat(5));
    public float RefreshRate=>NativeGraphics.DeviceDisplayStat(6)/1000f;
}
public sealed partial class GraphicsDevice
{
    public DisplayInfo Display {get;}=new DisplayInfo();
    public void ConfigureDisplay(DisplayOptions options)
    {
        if(options==null)throw new ArgumentNullException("options");
        if((int)options.Resolution<0||(int)options.Resolution>3||(int)options.Scan<0||(int)options.Scan>2)throw new ArgumentOutOfRangeException("options");
        int result=NativeGraphics.DeviceConfigureDisplay((int)options.Resolution,(int)options.Scan,options.Widescreen?1:0);
        if(result==-2)throw new OutOfMemoryException("Replacement display buffers could not be allocated; the current display is unchanged.");
        Check(result,"Display configuration requires an idle SDK device and a supported resolution/scan combination and cable.");
    }
}
