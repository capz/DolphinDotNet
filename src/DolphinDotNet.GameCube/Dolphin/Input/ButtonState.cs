namespace Dolphin.Input;

/// <summary>A button's stable state for one input update. Reads do not consume transitions.</summary>
public readonly struct ButtonState
{
    public bool IsHeld { get; }
    public bool WasPressed { get; }
    public bool WasReleased { get; }
    internal ButtonState(bool held, bool pressed, bool released)
    { IsHeld=held; WasPressed=pressed; WasReleased=released; }
}
