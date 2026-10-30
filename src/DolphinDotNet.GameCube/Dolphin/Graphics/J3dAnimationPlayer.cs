using System;
using System.Collections.Generic;
using Dolphin.Formats;

namespace Dolphin.Graphics;

/// <summary>Reusable opt-in J3D material animation playback. Resolves BTK, BTP
/// and BRK tracks against J3D MAT3 material names at setup time; BVA's shape
/// mapping remains explicit. Material channel evaluation allocates no arrays
/// per frame once bound.</summary>
public sealed class J3dAnimationPlayer
{
    private readonly struct Binding
    {
        public readonly J3dMaterialAnimation Animation;
        public readonly int Material, Track;
        public readonly bool Konst;
        public Binding(J3dMaterialAnimation anim, int material, int track, bool konst)
        { Animation = anim; Material = material; Track = track; Konst = konst; }
    }

    private readonly List<Binding> bindings = new List<Binding>();
    private readonly Dictionary<string,int> materialLookup =
        new Dictionary<string,int>(StringComparer.Ordinal);
    public J3dMaterialChannels Channels { get; }
    public float ElapsedSeconds { get; private set; }
    public float FramesPerSecond { get; }
    public bool Playing { get; private set; } = true;

    public J3dAnimationPlayer(Model model, string[] modelMaterialNames, float fps = 30f)
    {
        if (model == null) throw new ArgumentNullException(nameof(model));
        if (modelMaterialNames == null) throw new ArgumentNullException(nameof(modelMaterialNames));
        if (modelMaterialNames.Length != model.MaterialCount)
            throw new ArgumentException("MAT3 material names must match imported model order.");
        if (float.IsNaN(fps) || float.IsInfinity(fps) || fps <= 0 || fps > 240)
            throw new ArgumentOutOfRangeException(nameof(fps));
        FramesPerSecond = fps;
        Channels = new J3dMaterialChannels(model);
        for (int i = 0; i < modelMaterialNames.Length; i++)
        {
            string name = modelMaterialNames[i] ?? throw new ArgumentException("Null material name.");
            if (!materialLookup.TryAdd(name, i))
                throw new ArgumentException("Duplicate MAT3 material name: " + name);
        }
    }

    public void BindByName(J3dMaterialAnimation animation, bool konst = false)
    {
        if (animation == null) throw new ArgumentNullException(nameof(animation));
        if (animation.FileType == "bva1")
            throw new NotSupportedException("BVA visibility is shape-indexed; call BindShapeVisibility explicitly.");
        int tracks = animation.GetTrackCount(konst);
        for (int i = 0; i < tracks; i++)
        {
            string name = animation.GetTrackMaterialName(i, konst);
            if (!materialLookup.ContainsKey(name))
                throw new InvalidOperationException("J3D animation references unknown material: " + name);
        }
        for (int i = 0; i < tracks; i++)
        {
            int index = materialLookup[animation.GetTrackMaterialName(i, konst)];
            bindings.Add(new Binding(animation, index, i, konst));
        }
        Refresh();
    }

    /// <summary>Manual mapping required for BVA because its tracks target shapes,
    /// and multiple shapes can share one imported material.</summary>
    public void BindShapeVisibility(J3dMaterialAnimation bva, int sourceShape, int material)
    {
        if (bva == null) throw new ArgumentNullException(nameof(bva));
        if (bva.FileType != "bva1") throw new ArgumentException("Expected BVA source.", nameof(bva));
        if (material < 0 || material >= Channels.MaterialCount) throw new ArgumentOutOfRangeException(nameof(material));
        if (sourceShape < 0 || sourceShape >= bva.TrackCount) throw new ArgumentOutOfRangeException(nameof(sourceShape));
        bindings.Add(new Binding(bva, material, sourceShape, false));
        Refresh();
    }

    public void Seek(float seconds)
    {
        if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0)
            throw new ArgumentOutOfRangeException(nameof(seconds));
        ElapsedSeconds = seconds;
        Refresh();
    }

    public void Update(float deltaSeconds)
    {
        if (float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds) ||
            deltaSeconds < 0 || deltaSeconds > 0.25f)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        if (!Playing) return;
        ElapsedSeconds += deltaSeconds;
        Refresh();
    }

    public void Pause() => Playing = false;
    public void Resume() => Playing = true;
    public void Restart() { ElapsedSeconds = 0; Playing = true; Refresh(); }

    private void Refresh()
    {
        Channels.Reset();
        float frame = ElapsedSeconds * FramesPerSecond;
        foreach (var binding in bindings)
            Channels.Apply(binding.Animation, binding.Material, binding.Track, frame, binding.Konst);
    }

    public void Draw(Renderer renderer, ModelInstance instance, Transform world)
    {
        if (renderer == null) throw new ArgumentNullException(nameof(renderer));
        renderer.DrawModelAnimated(instance, world, Channels);
    }
}
