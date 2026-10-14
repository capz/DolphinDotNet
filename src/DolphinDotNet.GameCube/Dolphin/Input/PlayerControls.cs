using System;

namespace Dolphin.Input;

public enum ControllerStick { Stick, CStick }

/// <summary>Typed bindings configured once or changed for remapping. No reflection or string action names.</summary>
public sealed class PlayerBindings
{
    public ControllerButton Jump { get; set; } = ControllerButton.A;
    public ControllerButton Interact { get; set; } = ControllerButton.X;
    public ControllerButton Run { get; set; } = ControllerButton.B;
    public ControllerButton Pause { get; set; } = ControllerButton.Start;
    public ControllerStick Move { get; set; } = ControllerStick.Stick;
    public ControllerStick Look { get; set; } = ControllerStick.CStick;
}

public readonly struct PlayerInputState
{
    public ControllerState Controller { get; }
    public StickState Move { get; }
    public StickState Look { get; }
    public ButtonState Jump { get; }
    public ButtonState Interact { get; }
    public ButtonState Run { get; }
    public ButtonState Pause { get; }
    internal PlayerInputState(ControllerState state, PlayerBindings bindings)
    {
        Controller=state; Move=Select(state,bindings.Move); Look=Select(state,bindings.Look);
        Jump=state[bindings.Jump]; Interact=state[bindings.Interact];
        Run=state[bindings.Run]; Pause=state[bindings.Pause];
    }
    private static StickState Select(ControllerState state, ControllerStick stick)
    {
        if (stick==ControllerStick.Stick) return state.Stick;
        if (stick==ControllerStick.CStick) return state.CStick;
        throw new ArgumentOutOfRangeException("stick");
    }
}

/// <summary>Player-specific action snapshots, reading the shared controller update without polling.</summary>
public sealed class PlayerControls
{
    public int Port { get; }
    public PlayerBindings Bindings { get; }
    public PlayerControls(int port=0, PlayerBindings? bindings=null)
    { Controller.ValidatePort(port); Port=port; Bindings=bindings ?? new PlayerBindings(); }
    public PlayerInputState GetState() => GetState(Controller.GetState(Port));
    public PlayerInputState GetState(ControllerState state) => new PlayerInputState(state,Bindings);
    /// <summary>Map only input still available to this context.</summary>
    public PlayerInputState GetState(InputFrame input)
    {
        if (input==null) throw new ArgumentNullException("input");
        return GetState(input.State);
    }
}
