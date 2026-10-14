using System;

namespace Dolphin.Input;

/// <summary>An immutable, allocation-free snapshot of a controller input update.</summary>
public readonly struct ControllerState
{
    private readonly int flags, held, pressed, released;
    public ControllerButton HeldButtons => (ControllerButton)held;
    public ControllerButton PressedButtons => (ControllerButton)pressed;
    public ControllerButton ReleasedButtons => (ControllerButton)released;
    internal ControllerState Without(ControllerButton buttons, bool stick=false, bool cstick=false,
        bool leftTrigger=false, bool rightTrigger=false) =>
        new ControllerState(flags, held & ~(int)buttons, pressed & ~(int)buttons, released & ~(int)buttons,
            stick ? default : Stick, cstick ? default : CStick,
            leftTrigger ? 0 : LeftTrigger, rightTrigger ? 0 : RightTrigger);
    internal static void ValidateButtons(ControllerButton buttons)
    {
        if (((int)buttons & ~(int)ControllerButton.All)!=0)
            throw new ArgumentOutOfRangeException("buttons");
    }
    public bool IsConnected => (flags & 1)!=0;
    public bool WasConnected => (flags & 2)!=0;
    public bool WasDisconnected => (flags & 4)!=0;
    public bool SupportsRumble => (flags & 8)!=0;
    public StickState Stick { get; }
    public StickState CStick { get; }
    public float LeftTrigger { get; }
    public float RightTrigger { get; }
    internal ControllerState(int flags, int held, int pressed, int released,
        StickState stick, StickState cstick, float leftTrigger, float rightTrigger)
    {
        this.flags=flags; this.held=held; this.pressed=pressed; this.released=released;
        Stick=stick; CStick=cstick; LeftTrigger=leftTrigger; RightTrigger=rightTrigger;
    }
    public ButtonState this[ControllerButton button]
    {
        get
        {
            int mask=(int)button;
            if (mask<=0 || (mask & ~0x1f7f)!=0 || (mask & (mask-1))!=0)
                throw new ArgumentOutOfRangeException("button");
            return new ButtonState((held & mask)!=0, (pressed & mask)!=0, (released & mask)!=0);
        }
    }
    public ButtonState A => this[ControllerButton.A];
    public ButtonState B => this[ControllerButton.B];
    public ButtonState X => this[ControllerButton.X];
    public ButtonState Y => this[ControllerButton.Y];
    public ButtonState Z => this[ControllerButton.Z];
    public ButtonState L => this[ControllerButton.L];
    public ButtonState R => this[ControllerButton.R];
    public ButtonState Start => this[ControllerButton.Start];
    public ButtonState DPadUp => this[ControllerButton.DPadUp];
    public ButtonState DPadDown => this[ControllerButton.DPadDown];
    public ButtonState DPadLeft => this[ControllerButton.DPadLeft];
    public ButtonState DPadRight => this[ControllerButton.DPadRight];
}
