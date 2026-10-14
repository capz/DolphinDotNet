using System;

namespace Dolphin.Input;

/// <summary>Shared, consumable input for one router dispatch. Do not retain this object between updates.</summary>
public sealed class InputFrame
{
    private ControllerState state;
    internal InputFrame() { }
    internal void Reset(ControllerState value) { state=value; }
    /// <summary>An immutable copy of the input still available to subsequent handlers.</summary>
    public ControllerState State => state;
    public ButtonState this[ControllerButton button] => state[button];
    /// <summary>Hide these physical buttons, including held and release state, for this dispatch.</summary>
    public void Consume(ControllerButton buttons)
    {
        ControllerState.ValidateButtons(buttons);
        state=state.Without(buttons);
    }
    public void ConsumeStick(ControllerStick stick)
    {
        if (stick!=ControllerStick.Stick && stick!=ControllerStick.CStick)
            throw new ArgumentOutOfRangeException("stick");
        state=state.Without(ControllerButton.None, stick==ControllerStick.Stick, stick==ControllerStick.CStick);
    }
    public void ConsumeLeftTrigger() => state=state.Without(ControllerButton.None, false, false, true);
    public void ConsumeRightTrigger() => state=state.Without(ControllerButton.None, false, false, false, true);
    public void ConsumeAll() => state=state.Without(ControllerButton.All, true, true, true, true);
}
