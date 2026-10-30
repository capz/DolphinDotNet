using System;

namespace Dolphin.Graphics;

public enum TextureWrapMode { Clamp = 0, Repeat = 1, Mirror = 2 }
public enum TextureMinFilter
{
    Nearest = 0, Linear = 1, NearestMipmapNearest = 2,
    LinearMipmapNearest = 3, NearestMipmapLinear = 4, LinearMipmapLinear = 5
}
public enum TextureMagFilter { Nearest = 0, Linear = 1 }

public sealed partial class Texture2D
{
    /// <summary>Set independent GX wrap, minification, magnification and LOD bias.
    /// Between frames only; sampler changes invalidate the native draw cache.
    /// Mipmap filtering requires a texture that carries more than one level.</summary>
    public void ConfigureSampler(TextureWrapMode wrapS, TextureWrapMode wrapT,
        TextureMinFilter minFilter, TextureMagFilter magFilter, float lodBias = 0)
    {
        Check();
        if ((int)wrapS < 0 || (int)wrapS > 2 ||
            (int)wrapT < 0 || (int)wrapT > 2 ||
            (int)minFilter < 0 || (int)minFilter > 5 ||
            (int)magFilter < 0 || (int)magFilter > 1 ||
            float.IsNaN(lodBias) || float.IsInfinity(lodBias) ||
            lodBias < -4 || lodBias > 3.99f ||
            (MipLevels <= 1 && (int)minFilter >= 2))
            throw new ArgumentOutOfRangeException(nameof(minFilter),
                "GX sampler filter, wrapping, mip count or LOD bias is unsupported.");
        GraphicsDevice.Check(NativeGraphics.TextureSampler(Token, (int)wrapS,
            (int)wrapT, (int)minFilter, (int)magFilter, lodBias),
            "Texture sampler can only be changed outside a frame.");
    }
}
