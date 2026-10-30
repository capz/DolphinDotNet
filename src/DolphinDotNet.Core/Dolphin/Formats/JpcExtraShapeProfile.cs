using System;
using System.IO;
using static Dolphin.Formats.GameCubeBinary;

namespace Dolphin.Formats;

/// <summary>Bounded JPAC2 ESP1 base scale/alpha curves. Extra shape rotation,
/// pivots, sinusoidal alpha and indirect TEV remain unsupported.</summary>
public sealed class JpcExtraShapeProfile
{
    public bool ScaleEnabled { get; private set; }
    public bool AlphaEnabled { get; private set; }
    public float ScaleInTime { get; private set; }
    public float ScaleOutTime { get; private set; }
    public float ScaleStartX { get; private set; }
    public float ScaleEndX { get; private set; }
    public float ScaleStartY { get; private set; }
    public float ScaleEndY { get; private set; }
    public float AlphaInTime { get; private set; }
    public float AlphaOutTime { get; private set; }
    public float AlphaStart { get; private set; }
    public float AlphaMiddle { get; private set; }
    public float AlphaEnd { get; private set; }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    public static JpcExtraShapeProfile? Read(JpcArchive archive, ushort particleId)
    {
        if (archive == null) throw new ArgumentNullException(nameof(archive));
        foreach (var particle in archive.Particles)
        {
            if (particle.Id != particleId) continue;
            JpcExtraShapeProfile? profile = null;
            foreach (var chunk in particle.Chunks)
            {
                if (chunk.Type != "ESP1") continue;
                if (profile != null) throw new InvalidDataException("Duplicate JPA ESP1.");
                if (archive.Version != "JPAC2-10")
                    throw new NotSupportedException("JPAC1 extra shape variant not yet supported.");
                byte[] bytes = archive.CopyChunk(chunk);
                Check(bytes, 0, 0x60);
                uint flags = U32(bytes, 8);
                // Accept only base S/T scaling, linear alpha and no rotation,
                // pivot, looping scale, randomness or sine modulation.
                if ((flags & ~0x10003u) != 0)
                    throw new NotSupportedException("JPA ESP1 contains unimplemented scale, pivot, rotation or wave flags.");
                float[] f = new float[13];
                int[] offsets = { 12, 16, 20, 24, 28, 32, 44, 48, 52, 56, 60, 36, 72 };
                for (int i=0;i<f.Length;i++)
                {
                    f[i] = F32(bytes, offsets[i]);
                    if (!Finite(f[i]) || Math.Abs(f[i]) > 1024)
                        throw new InvalidDataException("JPA ESP1 contains invalid parameters.");
                }
                if (f[0] < 0 || f[0] > 1 || f[1] < f[0] || f[1] > 1 ||
                    f[6] < 0 || f[6] > 1 || f[7] < f[6] || f[7] > 1 ||
                    f[11] != 0 || f[12] != 0)
                    throw new NotSupportedException("JPA extra shape uses unsupported timing or randomness.");
                profile = new JpcExtraShapeProfile
                {
                    ScaleEnabled = (flags & 1u) != 0, AlphaEnabled = (flags & 0x10000u) != 0,
                    ScaleInTime = f[0], ScaleOutTime = f[1],
                    ScaleStartX = f[2], ScaleEndX = f[3], ScaleStartY = f[4], ScaleEndY = f[5],
                    AlphaInTime = f[6], AlphaOutTime = f[7],
                    AlphaStart = f[8], AlphaMiddle = f[9], AlphaEnd = f[10]
                };
            }
            return profile;
        }
        throw new ArgumentException("Unknown JPA particle.", nameof(particleId));
    }

    private static float Interpolate(float start,float end,float time)
        => start + (end-start)*Math.Max(0, Math.Min(1,time));

    public void Sample(float ageFraction,out float scaleX,out float scaleY,out float alpha)
    {
        if (!Finite(ageFraction) || ageFraction<0 || ageFraction>1)
            throw new ArgumentOutOfRangeException(nameof(ageFraction));
        scaleX=scaleY=alpha=1;
        if (ScaleEnabled)
        {
            float time = ScaleOutTime <= 0 ? 1 :
                Math.Max(0, Math.Min(1, ageFraction/ScaleOutTime));
            scaleX = Interpolate(ScaleStartX, ScaleEndX, time);
            scaleY = Interpolate(ScaleStartY, ScaleEndY, time);
        }
        if (AlphaEnabled)
        {
            alpha = ageFraction < AlphaInTime && AlphaInTime > 0 ?
                Interpolate(AlphaStart,AlphaMiddle,ageFraction/AlphaInTime) :
                ageFraction > AlphaOutTime && AlphaOutTime < 1 ?
                Interpolate(AlphaMiddle,AlphaEnd,(ageFraction-AlphaOutTime)/(1-AlphaOutTime)) :
                AlphaMiddle;
            alpha=Math.Max(0,Math.Min(1,alpha));
        }
    }
}
