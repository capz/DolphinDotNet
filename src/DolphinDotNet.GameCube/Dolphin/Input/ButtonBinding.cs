using System;

namespace Dolphin.Input;

/// <summary>A mutable physical-button binding for any game-defined action.</summary>
public sealed class ButtonBinding
{
    private ControllerButton button;
    public ControllerButton Button
    {
        get => button;
        set
        {
            ControllerState.ValidateButtons(value);
            int mask=(int)value;
            if (mask==0 || (mask & (mask-1))!=0) throw new ArgumentOutOfRangeException("value");
            button=value;
        }
    }
    public ButtonBinding(ControllerButton button) { Button=button; }
    public ButtonState GetState(InputFrame input)
    {
        if (input==null) throw new ArgumentNullException("input");
        return input[button];
    }
    public ButtonState GetState(ControllerState state) => state[button];
    public void Consume(InputFrame input)
    {
        if (input==null) throw new ArgumentNullException("input");
        input.Consume(button);
    }
}
