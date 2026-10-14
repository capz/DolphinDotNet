using System;

namespace Dolphin.Graphics;

/// <summary>A placement transform and optional owned model instance. The shared Model is separately owned.</summary>
public sealed class GameObject : IDisposable
{
    private bool disposed;

    public Transform Transform { get; } = new Transform();
    public ModelInstance? Model { get; }

    /// <summary>Transfers ownership of the supplied instance; an empty object can serve as a hierarchy root.</summary>
    public GameObject(ModelInstance? model = null) => Model = model;

    public void Update(float deltaSeconds)
    {
        Check();
        if (Model != null) Model.Update(deltaSeconds);
    }

    internal void Check()
    {
        if (disposed) throw new ObjectDisposedException("GameObject");
    }

    public void Dispose()
    {
        if (disposed) return;
        // Mark disposed only after native disposal succeeds, so active-frame failures can be retried.
        if (Model != null) Model.Dispose();
        disposed = true;
    }
}
