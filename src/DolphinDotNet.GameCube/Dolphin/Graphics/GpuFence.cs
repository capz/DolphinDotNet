namespace Dolphin.Graphics;

/// <summary>Frame-bound completion marker. This is not a sub-frame GX timing query.</summary>
public readonly struct GpuFence
{
    private readonly int value;
    internal GpuFence(int value){this.value=value;}
    public bool IsValid=>value!=0;
    public bool IsCompleted=>IsValid && NativeGraphics.DeviceFenceCompleted(value)!=0;
}
