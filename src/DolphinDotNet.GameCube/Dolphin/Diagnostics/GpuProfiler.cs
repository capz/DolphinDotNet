namespace Dolphin.Diagnostics;

/// <summary>GX submission telemetry (not an accurate GPU-timing or hardware cache-miss query).
/// Counters reset at device BeginFrame, and count backend vertex draw operations
/// and attempted texture bindings, including native model draws.</summary>
public static class GpuProfiler
{
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
