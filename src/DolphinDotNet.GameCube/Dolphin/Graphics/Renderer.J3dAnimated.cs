using System;

namespace Dolphin.Graphics;

public sealed partial class Renderer
{
    /// <summary>Opt-in per-material GX texture/color/visibility animation.
    /// Channel array is validated in native code before any GX command is issued.
    /// The source model's material data and standard DrawModel behavior are unchanged.</summary>
    public void DrawModelAnimated(ModelInstance instance, Transform transform, J3dMaterialChannels channels)
    {
        if (transform == null) throw new ArgumentNullException(nameof(transform));
        DrawModelAnimated(instance, transform.WorldMatrix, channels);
    }

    public void DrawModelAnimated(ModelInstance instance, float[] world, J3dMaterialChannels channels)
    {
        if (instance == null) throw new ArgumentNullException(nameof(instance));
        if (world == null) throw new ArgumentNullException(nameof(world));
        if (channels == null) throw new ArgumentNullException(nameof(channels));
        instance.Check();
        if (channels.MaterialCount != instance.Model.MaterialCount)
            throw new ArgumentException("Animation channel count differs from model material count.", nameof(channels));
        GraphicsDevice.Check(NativeModel.ModelDrawAnimated(instance.Token, world, channels.Values),
            "Invalid J3D animation state, model, transform or 3D pass.");
    }
}
