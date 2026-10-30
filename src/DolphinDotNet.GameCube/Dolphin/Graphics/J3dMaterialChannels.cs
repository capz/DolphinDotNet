using System;
using Dolphin.Formats;

namespace Dolphin.Graphics;

/// <summary>Per-material overrides for opt-in GX rendering of J3D animations.
/// Uses one reusable bounded float array, independently of the source model's
/// immutable package. Index mappings must be verified against imported MAT3.</summary>
public sealed class J3dMaterialChannels
{
    internal readonly float[] Values;
    private readonly float[] scratchColor = new float[4];
    private readonly float[] scratchUv = new float[9];
    private readonly float[] scratchCenter = new float[2];
    public int MaterialCount { get; }

    public J3dMaterialChannels(Model model) : this(model != null ? model.MaterialCount : throw new ArgumentNullException(nameof(model))) { }

    public J3dMaterialChannels(int materialCount)
    {
        if (materialCount < 1 || materialCount > 256) throw new ArgumentOutOfRangeException(nameof(materialCount));
        MaterialCount = materialCount;
        Values = new float[materialCount * 12];
        Reset();
    }

    /// <summary>Copy one material's channel record for diagnostics and synthetic tests.</summary>
    public void CopyMaterialState(int material, float[] destination)
    {
        if (destination == null || destination.Length < 12)
            throw new ArgumentException("Twelve floats required.", nameof(destination));
        Array.Copy(Values, Start(material), destination, 0, 12);
    }

    public void Reset()
    {
        for (int i = 0; i < MaterialCount; i++)
        {
            int offset = i * 12;
            Values[offset] = 1;
            for (int c = 1; c <= 4; c++) Values[offset + c] = 1;
            for (int c = 5; c <= 10; c++) Values[offset + c] = 0;
            Values[offset + 5] = 1;
            Values[offset + 9] = 1;
            Values[offset + 11] = -1;
        }
    }

    private int Start(int material)
    {
        if (material < 0 || material >= MaterialCount)
            throw new ArgumentOutOfRangeException(nameof(material));
        return material * 12;
    }

    public void SetVisibility(int material, bool visible) => Values[Start(material)] = visible ? 1 : 0;

    public void SetColor(int material, Color32 color)
    {
        int at = Start(material);
        Values[at + 1] = color.R / 255f;
        Values[at + 2] = color.G / 255f;
        Values[at + 3] = color.B / 255f;
        Values[at + 4] = color.A / 255f;
    }

    /// <summary>Use a source model TEX1 texture index; -1 retains its default.</summary>
    public void SetTextureIndex(int material, int index)
    {
        if (index < -1 || index > 4095) throw new ArgumentOutOfRangeException(nameof(index));
        Values[Start(material) + 11] = index;
    }

    /// <summary>Apply an affine 2D texture transformation to UV0.</summary>
    public void SetUvAffine(int material, float m00, float m01, float m02,
        float m10, float m11, float m12)
    {
        ValidateUv(m00); ValidateUv(m01); ValidateUv(m02);
        ValidateUv(m10); ValidateUv(m11); ValidateUv(m12);
        int at = Start(material) + 5;
        Values[at] = m00; Values[at + 1] = m01; Values[at + 2] = m02;
        Values[at + 3] = m10; Values[at + 4] = m11; Values[at + 5] = m12;
    }

    private static void ValidateUv(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value) || Math.Abs(value) > 4096)
            throw new ArgumentOutOfRangeException(nameof(value), "UV transform is not finite and bounded.");
    }

    /// <summary>Apply one indexed animation track to one source material.
    /// BVA tracks are authored per shape, so map them only when the target
    /// material's shapes share that visibility state.</summary>
    public void Apply(J3dMaterialAnimation animation, int material, int track, float frame, bool konst = false)
    {
        if (animation == null) throw new ArgumentNullException(nameof(animation));
        switch (animation.FileType)
        {
            case "bva1":
                SetVisibility(material, animation.SampleVisibility(track, frame));
                break;
            case "btp1":
                SetTextureIndex(material, animation.SampleTextureIndex(track, frame));
                break;
            case "brk1":
                animation.SampleColor(track, konst, frame, scratchColor);
                byte Clamp(float value) => (byte)Math.Max(0, Math.Min(255, Math.Round(value)));
                SetColor(material, new Color32(Clamp(scratchColor[0]), Clamp(scratchColor[1]),
                    Clamp(scratchColor[2]), Clamp(scratchColor[3])));
                break;
            case "btk1":
                animation.SampleUv(track, frame, scratchUv);
                if (animation.IsMayaTextureMatrix)
                    throw new NotSupportedException("Maya-mode BTK texture matrices require separate GX conversion.");
                animation.GetUvCenter(track, scratchCenter);
                // J3D supports three S/T/Q rotations and multiple matrix modes.
                // This adapter accepts the ordinary affine S/T profile only.
                if (Math.Abs(scratchUv[6] - 1) > 0.001 || Math.Abs(scratchUv[7]) > 0.001 ||
                    Math.Abs(scratchUv[8]) > 0.001 ||
                    Math.Abs(scratchUv[1] - scratchUv[4]) > 0.001)
                    throw new NotSupportedException("Non-affine BTK S/T/Q texture transforms are unsupported.");
                int fraction = animation.TextureRotationFraction;
                if (fraction > 15) throw new NotSupportedException("Invalid BTK rotation scale.");
                double radians = scratchUv[1] * (1 << fraction) * Math.PI / 32768.0;
                float c = (float)Math.Cos(radians), s = (float)Math.Sin(radians);
                float m00 = c * scratchUv[0], m01 = -s * scratchUv[3],
                      m10 = s * scratchUv[0], m11 = c * scratchUv[3];
                float cx = scratchCenter[0], cy = scratchCenter[1];
                SetUvAffine(material, m00, m01, cx + scratchUv[2] - m00 * cx - m01 * cy,
                    m10, m11, cy + scratchUv[5] - m10 * cx - m11 * cy);
                break;
            default:
                throw new NotSupportedException("Unknown J3D animation type.");
        }
    }
}
