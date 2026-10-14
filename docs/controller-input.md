# Controller input

`Dolphin.Input.Controller` samples four GameCube ports. Native initialization is
owned by the platform. Call `Controller.Update()` once at the start of each game
update, then read immutable `ControllerState` snapshots freely. Reads do not poll
hardware, allocate managed objects, or consume transitions.

```csharp
Controller.Update();
var pad = Controller.GetState(0);
if (pad.IsConnected)
{
    Move(pad.Stick.X, pad.Stick.Y);
    Look(pad.CStick.X, pad.CStick.Y);
    if (pad.A.WasPressed) Jump();
    if (pad.Start.WasPressed) TogglePause();
}
```

`Move`, `Look`, `Jump` and `TogglePause` stand for application logic.
Controller methods require the application executor. Copied snapshots contain only
values and can be inspected on either executor. A background task must pass work
back to application logic before sampling or querying the controller service. Async scheduling remains
automatic and separate from input sampling.

## Snapshot contract

- Ports are zero-based, 0–3; invalid ports throw `ArgumentOutOfRangeException`.
- `IsConnected`, `WasConnected`, `WasDisconnected` and `SupportsRumble` describe
  the current update. Before the first update the state is disconnected.
- Button properties A/B/X/Y/Z/L/R/Start and DPadUp/Down/Left/Right expose
  `IsHeld`, `WasPressed`, `WasReleased`. Enum access is `pad[ControllerButton.A]`;
  the indexer accepts one physical button, not a combined mask.
- Transitions stay visible until the next update. Copies retain their values.
- Disconnect clears input, reports disconnection and stops rumble, without
  creating button-release actions. Reconnect establishes a baseline, suppressing
  press actions for buttons already held. libogc's connected sample mask is used;
  unavailable or transiently unreadable samples are treated as disconnected.
- Sticks use radial deadzones and a unit-circle magnitude limit; +X is right and
  +Y is up. Triggers range from 0 to 1. Digital L/R clicks remain separate.

## Calibration

Configure once per port; values take effect on the next update:

```csharp
Controller.Configure(0, new ControllerOptions {
    StickDeadZone = 0.15f, CStickDeadZone = 0.15f,
    StickMaximum = 80, CStickMaximum = 65,
    TriggerMinimum = 30, TriggerMaximum = 180
});
```

These are starting defaults, not measured guarantees for every controller.
Set maximum travel and trigger endpoints for the device. Deadzones must be in
[0,1), stick maxima in (0,127], and trigger endpoints must satisfy
0 <= minimum < maximum <= 255. NaN/infinite/out-of-range settings are rejected.
Configuration is copied; later changing the options object does not change input.

## Rumble

`Controller.Rumble(0, durationSeconds: 0.15f)` starts or replaces a timed on/off
rumble. Durations must be positive and at most 60 seconds. It returns false when
the sampled device lacks a motor or a native alarm cannot be created/armed.
`Controller.StopRumble(0)` cancels it immediately. libogc system alarms issue only
native motor commands; no managed code executes inside interrupt callbacks.
Expiry does not require another input update or a managed task pump. Disconnect
and generated AOT entry-point cleanup stop motors and release owned alarms.

## Player bindings

```csharp
var controls = new PlayerControls(0, new PlayerBindings {
    Jump = ControllerButton.A, Interact = ControllerButton.X,
    Run = ControllerButton.B, Pause = ControllerButton.Start,
    Move = ControllerStick.Stick, Look = ControllerStick.CStick
});
// After the shared Controller.Update():
var input = controls.GetState();
if (input.Jump.WasPressed) Jump();
```

Bindings can be changed for remapping. Previously returned action snapshots stay
unchanged. This small typed layer supports common actions; custom game-specific
maps can use the same controller snapshots. No reflection or string action names
are needed.

`ReadButtonsDown` remains a compatibility polling call and advances input itself;
avoid mixing it with snapshot reads within one game update.

## Validation

Native fixtures cover transitions, connection changes, radial deadzones,
calibration, trigger ranges, timed-rumble backend contracts and cleanup. The actual GameCube backend is also
compiled against a recording SDK fixture to verify alarm creation/arming failures,
capabilities, native callback arguments, interrupt protection and alarm removal. Compiled
managed tests exercise nested value snapshots, bindings, validation, repeated
reads, GC stress and rejection of background input access. CI compiles the actual
libogc backend and checks 32-bit AOT execution. Physical device calibration and
alarm-driven motor expiry still need hardware verification.
