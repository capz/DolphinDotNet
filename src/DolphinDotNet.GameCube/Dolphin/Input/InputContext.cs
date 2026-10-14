using System;

namespace Dolphin.Input;

/// <summary>A game-owned input handler. Modal contexts block every context below them.</summary>
public abstract class InputContext
{
    internal InputRouter? Owner;
    internal bool Active;
    public bool IsModal { get; }
    protected InputContext(bool isModal=true) { IsModal=isModal; }
    protected InputRouter Router => Owner ?? throw new InvalidOperationException("Context is not on a router.");
    public abstract void HandleInput(InputFrame input);
    /// <summary>Called when pushed or exposed by removing a blocking modal context.</summary>
    public virtual void OnActivated() { }
    /// <summary>Cancel owned actions here when removed or covered by a modal context.</summary>
    public virtual void OnDeactivated() { }
    /// <summary>Cancel held actions here; physical disconnects do not produce button-release events.</summary>
    public virtual void OnDisconnected() { }
}
