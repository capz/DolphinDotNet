namespace Dolphin.Graphics;
public sealed partial class GraphicsDevice
{
    private readonly float[] defaultSampling=new float[40];
    /// <summary>Set both texture slots' global sampler fallback between frames.
    /// Explicit material samplers take precedence. default restores texture-authored sampling.</summary>
    public void ConfigureSampling(SamplerState sampler)
    {
        Material.WriteSampler(defaultSampling,0,sampler,default);
        Material.WriteSampler(defaultSampling,20,sampler,default);
        Check(NativeGraphics.RenderDefaults(defaultSampling),"Global sampling requires an idle SDK device.");
    }
}
