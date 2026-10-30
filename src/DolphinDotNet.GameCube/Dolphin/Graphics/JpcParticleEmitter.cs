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
    private bool disposed;
    public JpcParticleSimulator Simulation { get; }
    public ushort ParticleId => Simulation.Definition.ParticleId;

    public JpcParticleEmitter(JpcArchive archive, ushort particleId, int maxParticles = 128, uint seed = 1)
    {
        if (archive == null) throw new ArgumentNullException(nameof(archive));
        var config = JpcEmitterDefinition.Read(archive, particleId);
        Simulation = new JpcParticleSimulator(config, maxParticles, seed);
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
        for (int i = 0; i < Simulation.ActiveCount; i++)
        {
            var p = Simulation.GetParticle(i);
            byte alpha = (byte)Math.Max(0, Math.Min(255, Math.Round(config.Alpha * p.Alpha)));
            if (alpha == 0) continue;
            material.Color = new Color32(config.Red, config.Green, config.Blue, alpha);
            renderer.DrawBillboard(billboard, camera, new Vector3(p.X, p.Y, p.Z), size, material);
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
