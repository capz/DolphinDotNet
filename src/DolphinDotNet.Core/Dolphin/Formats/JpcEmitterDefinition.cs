using System;
using System.IO;
using static Dolphin.Formats.GameCubeBinary;

namespace Dolphin.Formats;

/// <summary>
/// Conservative JPA emitter profile used by the GX billboard adapter.
/// Supports bounded BEM1 billboard volumes; advanced field, child,
/// rotation and shader animation profiles fail explicitly.
/// </summary>
public enum JpcEmitterVolume { Cube = 0, Sphere = 1, Cylinder = 2, Torus = 3, Point = 4, Circle = 5, Line = 6 }

public sealed class JpcEmitterDefinition
{
    public ushort ParticleId { get; }
    public float SpawnRate { get; }
    public float ParticleLifetimeFrames { get; }
    public float InitialVelocity { get; }
    public float VelocitySpread { get; }
    public float Drag { get; }
    public float SizeX { get; }
    public float SizeY { get; }
    public float PositionX { get; }
    public float PositionY { get; }
    public float PositionZ { get; }
    public byte Red { get; }
    public byte Green { get; }
    public byte Blue { get; }
    public byte Alpha { get; }
    public int TextureIndex { get; }
    public int MaxEmitterFrames { get; }
    public float DirectionX { get; }
    public float DirectionY { get; }
    public float DirectionZ { get; }
    public JpcEmitterVolume Volume { get; }
    public float VolumeSize { get; }
    public float EmitterScaleX { get; }
    public float EmitterScaleY { get; }
    public float EmitterScaleZ { get; }

    private JpcEmitterDefinition(ushort id, float rate, int life, float velocity, float spread,
        float drag, float sizeX, float sizeY, float x, float y, float z, float dirX,
        float dirY, float dirZ, int maxFrames, int textureIndex,
        JpcEmitterVolume volume, float volumeSize, float scaleX, float scaleY, float scaleZ,
        byte red, byte green, byte blue, byte alpha)
    {
        ParticleId = id; SpawnRate = rate; ParticleLifetimeFrames = life;
        InitialVelocity = velocity; VelocitySpread = spread; Drag = drag;
        SizeX = sizeX; SizeY = sizeY; PositionX = x; PositionY = y; PositionZ = z;
        DirectionX = dirX; DirectionY = dirY; DirectionZ = dirZ;
        MaxEmitterFrames = maxFrames; TextureIndex = textureIndex;
        Volume = volume; VolumeSize = volumeSize;
        EmitterScaleX = scaleX; EmitterScaleY = scaleY; EmitterScaleZ = scaleZ;
        Red = red; Green = green; Blue = blue; Alpha = alpha;
    }

    public static JpcEmitterDefinition Read(JpcArchive archive, ushort id)
    {
        if (archive == null) throw new ArgumentNullException(nameof(archive));
        JpcArchive.Particle? particle = null;
        foreach (var item in archive.Particles)
            if (item.Id == id) { particle = item; break; }
        if (particle == null) throw new ArgumentException("Unknown JPC particle ID.", nameof(id));
        byte[]? dynamics = null, shape = null;
        foreach (var chunk in particle.Chunks)
        {
            switch (chunk.Type)
            {
                case "BEM1":
                    if (dynamics != null) throw new InvalidDataException("Duplicate JPA dynamics.");
                    dynamics = archive.CopyChunk(chunk); break;
                case "BSP1":
                    if (shape != null) throw new InvalidDataException("Duplicate JPA shape.");
                    shape = archive.CopyChunk(chunk); break;
                case "TDB1": case "TEX1": case "ESP1": break;
                // Every additional particle behavior requires an intentional renderer profile.
                default: throw new NotSupportedException("Unsupported JPA emitter chunk: " + chunk.Type);
            }
        }
        if (dynamics == null || shape == null) throw new InvalidDataException("Missing JPA BEM1 or BSP1.");
        bool v2 = archive.Version == "JPAC2-10";
        Check(dynamics, 0, v2 ? 0x7C : 0x8C);
        Check(shape, 0, v2 ? 0x34 : 0x60);
        if (Ascii(dynamics, 0, 4) != "BEM1" || Ascii(shape, 0, 4) != "BSP1")
            throw new InvalidDataException("JPA chunk magic mismatch.");
        // Only the basic volume / billboard style is implemented.
        // gclib/bunfoe reads packed bitfields starting at the least significant bit.
        uint dynamicsFlags = U32(dynamics, v2 ? 8 : 12);
        uint shapeFlags = U32(shape, v2 ? 8 : 12);
        // An unrecognized shape or emitter volume must not silently be rendered incorrectly.
        int volume = (int)((dynamicsFlags >> 8) & 7);
        int shapeType = (int)(shapeFlags & 15);
        if (shapeType != 2 || volume == 3 || volume > 6)
            throw new NotSupportedException("Only billboard emitters with simple volumes are supported.");
        // Reject emitter configurations which have field/child or scroll dependencies.
        if ((shapeFlags & (1u << 24)) != 0)
            throw new NotSupportedException("JPA texture-scrolling effects require a GX texture-matrix profile.");
        int rateOff = v2 ? 0x4C : 0x1C;
        int lifeOff = v2 ? 0x72 : 0x2A;
        int velOff = v2 ? 0x34 : 0x30;
        int spreadOff = v2 ? 0x44 : 0x44;
        int resistOff = v2 ? 0x60 : 0x48;
        int posOff = v2 ? 0x1C : 0x6C;
        int dirOff = v2 ? 0x28 : 0x78;
        int shapeSizeOff = v2 ? 0x10 : 0x14;
        int colorOff = v2 ? 0x26 : 0x2C;
        int textureOff = v2 ? 0x20 : 0x26;
        float rate = F32(dynamics, rateOff), speed = F32(dynamics, velOff);
        float spread = F32(dynamics, spreadOff), drag = F32(dynamics, resistOff);
        float sizeX = F32(shape, shapeSizeOff), sizeY = F32(shape, shapeSizeOff + 4);
        int life = S16(dynamics, lifeOff);
        int max = S16(dynamics, v2 ? 0x6E : 0x26);
        float x = F32(dynamics, posOff), y = F32(dynamics, posOff + 4), z = F32(dynamics, posOff + 8);
        float dx = F32(dynamics, dirOff), dy = F32(dynamics, dirOff + 4), dz = F32(dynamics, dirOff + 8);
        float sweep = F32(dynamics, v2 ? 0x58 : 0x10);
        float minRadius = F32(dynamics, v2 ? 0x5C : 0x14);
        float volumeSize = U16(dynamics, v2 ? 0x74 : 0x18);
        int scaleOff = v2 ? 0x10 : 0x60;
        float scaleX = F32(dynamics, scaleOff), scaleY = F32(dynamics, scaleOff + 4),
              scaleZ = F32(dynamics, scaleOff + 8);
        if (volume != 4 && (!Finite(sweep) || sweep != 0 || !Finite(minRadius) || minRadius != 0))
            throw new NotSupportedException("Volume sweeps and inner radii require original JPAC distribution rules.");
        if (!Finite(rate) || rate < 0 || rate > 1024 || !Finite(speed) || Math.Abs(speed) > 1000 ||
            !Finite(spread) || spread < 0 || spread > 1000 ||
            !Finite(drag) || drag < 0 || drag > 2 ||
            !Finite(sizeX) || !Finite(sizeY) || sizeX <= 0 || sizeY <= 0 || sizeX > 1024 || sizeY > 1024 ||
            !Finite(x) || !Finite(y) || !Finite(z) ||
            !Finite(dx) || !Finite(dy) || !Finite(dz) ||
            dx * dx + dy * dy + dz * dz < 0.00001f || life <= 0 || life > 32767 ||
            (volume != 4 && (volumeSize < 1 || volumeSize > 4096 ||
             !Finite(scaleX) || !Finite(scaleY) || !Finite(scaleZ) ||
             scaleX <= 0 || scaleY <= 0 || scaleZ <= 0 ||
             scaleX > 1024 || scaleY > 1024 || scaleZ > 1024)))
            throw new InvalidDataException("Invalid JPA emitter parameter.");
        int localTexture = shape[textureOff];
        int texIndex = particle.TextureIds.Count > 0
            ? localTexture < particle.TextureIds.Count ? particle.TextureIds[localTexture] : -1
            : -1;
        if (texIndex < 0 || texIndex >= archive.Textures.Count)
            throw new InvalidDataException("JPA billboard has no valid texture.");
        return new JpcEmitterDefinition(id, rate, life, speed, spread, drag,
            sizeX, sizeY, x, y, z, dx, dy, dz, max, texIndex,
            (JpcEmitterVolume)volume, volumeSize, scaleX, scaleY, scaleZ,
            shape[colorOff], shape[colorOff + 1], shape[colorOff + 2], shape[colorOff + 3]);
    }

    private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
}
