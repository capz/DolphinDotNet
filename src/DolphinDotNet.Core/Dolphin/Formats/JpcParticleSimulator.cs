using System;
using System.IO;

namespace Dolphin.Formats;

/// <summary>Bounded deterministic 60 Hz simulation for a supported JPA point emitter.
/// Graphics allocations, native GX submission and caller camera state are separate.</summary>
public sealed class JpcParticleSimulator
{
    public struct ParticleState
    {
        public float X, Y, Z;
        public float Vx, Vy, Vz;
        public float AgeFrames, LifetimeFrames;
        public float Alpha => LifetimeFrames > 0 ?
            Math.Max(0, Math.Min(1, 1 - AgeFrames / LifetimeFrames)) : 0;
    }

    private readonly ParticleState[] particles;
    private readonly JpcEmitterDefinition definition;
    private uint random;
    private float frameAccumulator, spawnRemainder;
    private int active, frames;
    private bool emitting = true;
    public int Capacity => particles.Length;
    public int ActiveCount => active;
    public JpcEmitterDefinition Definition => definition;
    public bool IsEmitting => emitting;
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    public JpcParticleSimulator(JpcEmitterDefinition definition, int maxParticles = 128, uint seed = 1)
    {
        this.definition = definition ?? throw new ArgumentNullException(nameof(definition));
        if (maxParticles < 1 || maxParticles > 256)
            throw new ArgumentOutOfRangeException(nameof(maxParticles));
        particles = new ParticleState[maxParticles];
        random = seed == 0 ? 1 : seed;
    }

    public ParticleState GetParticle(int index)
    {
        if (index < 0 || index >= active) throw new ArgumentOutOfRangeException(nameof(index));
        return particles[index];
    }

    /// <summary>Stop spawning; existing particles remain until their lifetime ends.</summary>
    public void Stop() => emitting = false;

    public void Restart(uint seed = 1)
    {
        active = 0; frames = 0; frameAccumulator = 0; spawnRemainder = 0;
        random = seed == 0 ? 1 : seed; emitting = true;
    }

    public void Update(float deltaSeconds)
    {
        if (float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds) ||
            deltaSeconds < 0 || deltaSeconds > 0.25f)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        frameAccumulator += deltaSeconds * 60f;
        int stepCount = 0;
        while (frameAccumulator >= 1 && stepCount++ < 16)
        {
            frameAccumulator -= 1;
            Step();
        }
    }

    private float RandomSigned()
    {
        random ^= random << 13;
        random ^= random >> 17;
        random ^= random << 5;
        return (random & 0xFFFFFF) / 8388607.5f - 1f;
    }

    private void Step()
    {
        for (int i = 0; i < active;)
        {
            var p = particles[i];
            p.AgeFrames += 1;
            if (p.AgeFrames >= p.LifetimeFrames)
            {
                particles[i] = particles[--active];
                continue;
            }
            p.X += p.Vx; p.Y += p.Vy; p.Z += p.Vz;
            p.Vx *= definition.Drag;
            p.Vy *= definition.Drag;
            p.Vz *= definition.Drag;
            particles[i++] = p;
        }

        if (!emitting) return;
        if (definition.MaxEmitterFrames > 0 && frames >= definition.MaxEmitterFrames)
        {
            emitting = false;
            return;
        }
        frames++;
        spawnRemainder += definition.SpawnRate;
        int spawn = (int)spawnRemainder;
        spawnRemainder -= spawn;
        for (int i = 0; i < spawn && active < particles.Length; i++)
        {
            float dx = definition.DirectionX, dy = definition.DirectionY, dz = definition.DirectionZ;
            float length = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
            dx = dx / length + RandomSigned() * definition.VelocitySpread;
            dy = dy / length + RandomSigned() * definition.VelocitySpread;
            dz = dz / length + RandomSigned() * definition.VelocitySpread;
            particles[active++] = new ParticleState
            {
                X = X + definition.PositionX, Y = Y + definition.PositionY, Z = Z + definition.PositionZ,
                Vx = dx * definition.InitialVelocity,
                Vy = dy * definition.InitialVelocity,
                Vz = dz * definition.InitialVelocity,
                AgeFrames = 0, LifetimeFrames = definition.ParticleLifetimeFrames
            };
        }
    }
}
