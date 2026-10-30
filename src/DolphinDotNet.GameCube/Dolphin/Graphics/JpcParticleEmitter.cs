using System;
using Dolphin.Formats;

namespace Dolphin.Graphics;

/// <summary>
/// Setup-time JPA billboard renderer using DolphinDotNet's existing GX mesh backend.
/// This supports the explicitly validated point/billboard emitter subset, not the
/// complete JParticle engine. Rendering must be inside an active 3D pass.
/// </summary>
public sealed class JpcParticleEmitter : IDisposable
{
    private readonly Texture2D texture;
    private readonly Billboard billboard;
    private readonly Material material;
    private readonly int[] drawOrder;
    private readonly float[] cameraDepths;
    private bool disposed;
    public JpcParticleSimulator Simulation { get; }
    public ushort ParticleId => Simulation.Definition.ParticleId;

    public JpcParticleEmitter(JpcArchive archive, ushort particleId, int maxParticles = 128, uint seed = 1)
    {
        if (archive == null) throw new ArgumentNullException(nameof(archive));
        var config = JpcEmitterDefinition.Read(archive, particleId);
        Simulation = new JpcParticleSimulator(config, maxParticles, seed);
        Simulation.ExtraShape = JpcExtraShapeProfile.Read(archive,particleId);
        drawOrder = new int[maxParticles];
        cameraDepths = new float[maxParticles];
        // Decode once during setup, never from Update or Draw.
        texture = Texture2D.FromBti(archive.Textures[config.TextureIndex].BtiData, true);
        try { billboard = new Billboard(); }
        catch { texture.Dispose(); throw; }
        material = new Material
        {
            Texture = texture,
            Blend = BlendMode.Alpha,
            Cull = CullMode.None,
            DepthTest = true,
            DepthWrite = false,
            Lighting = LightingMode.Unlit
        };
    }

    /// <summary>Apply an existing validated DNEF GX TEV effect to this
    /// particle emitter. Effect lifetime is caller-owned; configure outside frames.
    /// Multi-texture, indirect and game-specific BSP1 TEV recipes require
    /// source-material interpretation and are not inferred here.</summary>
    public void ConfigureTev(GxEffect? effect)
    {
        Check();
        if(effect != null)
        {
            effect.Check();
            if(effect.RequiresSecondaryTexture)
                throw new NotSupportedException("JPA effect requires a second texture.");
        }
        material.Effect=effect;
    }

    public BlendMode BlendMode
    {
        get { Check(); return material.Blend; }
        set
        {
            Check();
            if(value!=BlendMode.Alpha && value!=BlendMode.Additive &&
               value!=BlendMode.PremultipliedAlpha && value!=BlendMode.Opaque)
                throw new ArgumentOutOfRangeException(nameof(value));
            material.Blend=value;
        }
    }

    public void Update(float deltaSeconds)
    {
        Check();
        Simulation.Update(deltaSeconds);
    }

    public void Draw(Renderer renderer, Camera camera)
    {
        Check();
        if (renderer == null) throw new ArgumentNullException(nameof(renderer));
        if (camera == null) throw new ArgumentNullException(nameof(camera));
        var config = Simulation.Definition;
        var size = new Vector2(config.SizeX, config.SizeY);
        // This profile uses simple lifetime alpha. JPA's texture/color animation,
        // advanced blend modes, fields and child emitter effects are not approximated.
        // Sort translucent particles far-to-near using preallocated index arrays.
        // At most 256 entries: insertion sort is bounded and allocation-free.
        int count = Simulation.ActiveCount;
        var view = camera.View;
        for (int i = 0; i < count; i++)
        {
            var p = Simulation.GetParticle(i);
            float depth = view[2] * p.X + view[6] * p.Y + view[10] * p.Z + view[14];
            int insert = i;
            while (insert > 0 && cameraDepths[insert - 1] > depth)
            {
                cameraDepths[insert] = cameraDepths[insert - 1];
                drawOrder[insert] = drawOrder[insert - 1];
                insert--;
            }
            cameraDepths[insert] = depth;
            drawOrder[insert] = i;
        }
        for (int i = 0; i < count; i++)
        {
            var p = Simulation.GetParticle(drawOrder[i]);
            float scaleX=1,scaleY=1,alphaFactor=1;
            Simulation.ExtraShape?.Sample(p.AgeFrames/p.LifetimeFrames,
                out scaleX,out scaleY,out alphaFactor);
            byte alpha = (byte)Math.Max(0, Math.Min(255, Math.Round(config.Alpha * p.Alpha * alphaFactor)));
            if (alpha == 0) continue;
            material.Color = new Color32(config.Red, config.Green, config.Blue, alpha);
            renderer.DrawBillboard(billboard, camera, new Vector3(p.X, p.Y, p.Z),
                new Vector2(size.X * scaleX, size.Y * scaleY), material);
        }
    }

    private void Check()
    {
        if (disposed) throw new ObjectDisposedException(nameof(JpcParticleEmitter));
    }

    public void Dispose()
    {
        if (disposed) return;
        billboard.Dispose();
        texture.Dispose();
        disposed = true;
    }
}
