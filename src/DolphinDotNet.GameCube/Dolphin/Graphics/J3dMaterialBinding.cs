using System;
using Dolphin.Formats;

namespace Dolphin.Graphics;

/// <summary>Optional bridge from sampled J3D material tracks to a caller-owned Material.
/// For imported models, use Renderer.DrawModelWithMaterial to render the override.
/// This does not reproduce per-submesh TEV state or texture-matrix animation.</summary>
public static class J3dMaterialBinding
{
    /// <summary>Apply a BTP texture selection or a BRK color. For BVA, returns
    /// false when the selected track is invisible so the caller can skip drawing.
    /// BTK UV matrices cannot be expressed by Material and fail explicitly.</summary>
    public static bool Apply(J3dMaterialAnimation animation, Material material, float frame,
        int trackIndex = 0, Texture2D[]? textures = null, bool konstColor = false)
    {
        if (animation == null) throw new ArgumentNullException(nameof(animation));
        if (material == null) throw new ArgumentNullException(nameof(material));
        switch (animation.FileType)
        {
            case "btp1":
                if (textures == null) throw new ArgumentNullException(nameof(textures));
                int selected = animation.SampleTextureIndex(trackIndex, frame);
                if (selected >= textures.Length || textures[selected] == null)
                    throw new ArgumentException("BTP texture selection is outside supplied textures.");
                textures[selected].Check();
                material.Texture = textures[selected];
                return true;
            case "brk1":
                var rgba = animation.SampleColor(trackIndex, konstColor, frame);
                byte Clamp(float value)
                {
                    if (float.IsNaN(value) || float.IsInfinity(value))
                        throw new ArgumentException("Invalid J3D color track value.");
                    return (byte)Math.Max(0, Math.Min(255, Math.Round(value)));
                }
                material.Color = new Color32(Clamp(rgba[0]), Clamp(rgba[1]),
                    Clamp(rgba[2]), Clamp(rgba[3]));
                return true;
            case "bva1":
                return animation.SampleVisibility(trackIndex, frame);
            case "btk1":
                throw new NotSupportedException("BTK requires GX texture-matrix support; use SampleUv to access source tracks.");
            default:
                throw new NotSupportedException("Unknown J3D material animation.");
        }
    }
}
