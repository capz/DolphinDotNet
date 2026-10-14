namespace Dolphin.Input;

/// <summary>Normalized radial stick position. Positive X is right; positive Y is up.</summary>
public readonly struct StickState
{
    public float X { get; }
    public float Y { get; }
    internal StickState(float x, float y) { X=x; Y=y; }
}
