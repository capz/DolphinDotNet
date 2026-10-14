using System;

namespace Dolphin.Input;

public enum ButtonCaptureStatus { Waiting, Completed, Cancelled }

/// <summary>Modal capture for one button binding. Pops itself on success or cancellation.</summary>
public sealed class ButtonCaptureContext : InputContext
{
    private readonly ButtonBinding binding;
    private readonly ControllerButton allowed, cancel;
    private bool waitingForRelease=true;
    public ButtonCaptureStatus Status { get; private set; }
    public ControllerButton CapturedButton { get; private set; }
    public ButtonCaptureContext(ButtonBinding binding, ControllerButton cancelButton=ControllerButton.B,
        ControllerButton allowedButtons=ControllerButton.All)
    {
        if (binding==null) throw new ArgumentNullException("binding");
        ControllerState.ValidateButtons(cancelButton);
        ControllerState.ValidateButtons(allowedButtons);
        int mask=(int)cancelButton;
        if ((mask & (mask-1))!=0) throw new ArgumentOutOfRangeException("cancelButton");
        if ((allowedButtons & ~cancelButton)==ControllerButton.None)
            throw new ArgumentOutOfRangeException("allowedButtons");
        this.binding=binding; cancel=cancelButton; allowed=allowedButtons;
    }
    public override void OnActivated()
    {
        Status=ButtonCaptureStatus.Waiting;
        CapturedButton=ControllerButton.None;
        waitingForRelease=true;
    }
    public override void OnDeactivated()
    {
        if (Status==ButtonCaptureStatus.Waiting) Status=ButtonCaptureStatus.Cancelled;
    }
    public override void OnDisconnected()
    {
        Status=ButtonCaptureStatus.Cancelled;
    }
    public override void HandleInput(InputFrame input)
    {
        var state=input.State;
        if (!state.IsConnected)
        {
            if (Status==ButtonCaptureStatus.Cancelled) Router.Remove(this);
            return;
        }
        // Use the physical snapshot: router suppression must not disguise a held opener.
        if (waitingForRelease)
        {
            if (Router.HeldButtons!=ControllerButton.None) return;
            waitingForRelease=false;
            return;
        }
        if (cancel!=ControllerButton.None && state[cancel].WasPressed)
        {
            Status=ButtonCaptureStatus.Cancelled;
            input.ConsumeAll();
            Router.Remove(this);
            return;
        }
        int pressed=(int)(state.PressedButtons & allowed & ~cancel);
        if (pressed==0) return;
        // Resolve simultaneous presses deterministically by physical button bit order.
        CapturedButton=(ControllerButton)(pressed & -pressed);
        binding.Button=CapturedButton;
        Status=ButtonCaptureStatus.Completed;
        input.ConsumeAll();
        Router.Remove(this);
    }
}
