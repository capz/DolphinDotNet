namespace Dolphin.Diagnostics;

/// <summary>GX submission telemetry (not an accurate GPU-timing or hardware cache-miss query).
/// Counters reset at device BeginFrame, and count backend vertex draw operations
/// and attempted texture bindings, including native model draws.</summary>
public static class GpuProfiler
{
    /// <summary>Native rectangle construction; keep the legacy path only for throughput validation.</summary>
    public static bool UseNative2DRectangles {set=>Dolphin.Graphics.Draw2D.DirectRectanglePath=value;}
    /// <summary>Choose the SDK bitmap console pipeline. Atlas is the default;
    /// the geometry modes remain available for controlled comparisons.</summary>
    public static OverlayGlyphMode OverlayGlyphs {
        set {if(value<OverlayGlyphMode.Pixels||value>OverlayGlyphMode.Atlas)throw new System.ArgumentOutOfRangeException("value");
            Dolphin.Graphics.NativeGraphics.PerformanceCompactGlyphs((int)value);}
    }
    /// <summary>Opt-in reuse of a completed same-color EFB clear. Raw GX invalidates the proof.</summary>
    public static bool ReuseFrameClear {set=>Dolphin.Graphics.NativeGraphics.PerformanceFrameReuse(value?1:0);}
    public static int BeginFrameCopyMicroseconds=>Dolphin.Graphics.NativeGraphics.PerformanceRenderStat(5);
    public static int GateReacquireMicroseconds=>Dolphin.Graphics.NativeGraphics.PerformanceRenderStat(6);
    public static bool BeginFrameCopied=>Dolphin.Graphics.NativeGraphics.PerformanceRenderStat(7)!=0;
    /// <summary>Last completed frame's blocking GX_DrawDone duration, in microseconds.
    /// Read after EndFrame and before the next BeginFrame. Includes outstanding work, not total GPU time.</summary>
    public static int GpuWaitMicroseconds => Dolphin.Graphics.NativeGraphics.PerformanceRenderStat(3);
    /// <summary>Last completed frame's VIDEO_WaitVSync duration in microseconds.</summary>
    public static int RetraceWaitMicroseconds => Dolphin.Graphics.NativeGraphics.PerformanceRenderStat(4);
    public static GpuSubmissionStatistics Snapshot()
        =>new GpuSubmissionStatistics(
            Dolphin.Graphics.NativeGraphics.PerformanceRenderStat(0),
            Dolphin.Graphics.NativeGraphics.PerformanceRenderStat(1),
            Dolphin.Graphics.NativeGraphics.PerformanceRenderStat(2));
}
public readonly struct GpuSubmissionStatistics
{
    public int DrawSubmissions {get;}
    public int TextureBindings {get;}
    public int Triangles {get;}
    internal GpuSubmissionStatistics(int drawSubmissions,int textureBindings,int triangles)
    {DrawSubmissions=drawSubmissions;TextureBindings=textureBindings;Triangles=triangles;}
}

/// <summary>SDK bitmap console pipelines. Atlas samples the same 5x7 font
/// from a 4 KiB nearest-filtered I4 texture using one quad per character.</summary>
public enum OverlayGlyphMode { Pixels, HorizontalRuns, Rectangles, Atlas }
