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

## Headless Dolphin acceptance

`tests/ControllerDolphinSmoke` runs the public managed controller API against
real emulated pads. `scripts/dolphin_controller_test.py` injects Dolphin pipe
inputs on all four ports and synchronizes cases using guest breakpoints. It
never writes input samples or expected results into guest RAM. The managed
fixture checks all 12 buttons, press/hold/release, a simultaneous chord, both
sticks in both directions, intermediate travel, diagonal normalization,
deadzones, both triggers at five travel levels and port isolation. Repeated
snapshot reads must preserve transitions. There are 285 cases.

CI builds the AOT fixture with `tests/build_controller_dolphin.sh`, runs it with
the headless Dolphin binary, and uploads the emulator log and passed-case list.
To rerun against matching artifacts:

```sh
python3 scripts/dolphin_controller_test.py controller.dol --elf controller.elf \
  --logs dolphin-controller --verify-rejection
```

This requires Linux, Dolphin's pipe input backend, `xvfb-run`, Python and
`readelf`. It verifies the emulated input path; physical controller calibration,
motor output and hot-unplug still require hardware. Native unit tests cover
disconnect/reconnect transitions and rumble alarm cleanup separately.
The negative control deliberately injects B instead of A and requires the
specific managed assertion failure before running the complete positive suite.
The suite passes on Dolphin 2503. It exposed and now guards the backend's
connection mask: `PAD_ScanPads()` reports low bits 0–3, whereas
`PAD_CHAN_BIT()` is the high-bit mask used by other PAD APIs. Intermediate stick
checks also exposed a compiler stack-join bug that truncated float conditionals;
typed arguments/locals now preserve floating-point values across those joins.

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

## Input contexts

`InputRouter` routes one player's shared snapshot through a bounded context stack.
Call it once per game tick after the shared hardware update:

```csharp
var router = new InputRouter(port: 0);
router.Push(new GameplayContext());

// Each game tick, on the application executor:
Controller.Update();
router.Update();
```

Contexts are game-owned classes. A context is modal by default: only it and any
non-modal contexts above it receive input. Use `base(isModal: false)` for an
input overlay that should pass unconsumed controls to lower contexts.
Handlers run from top to bottom; the first modal context ends routing.

```csharp
sealed class GameplayContext : InputContext
{
    private readonly ButtonBinding attack = new ButtonBinding(ControllerButton.A);
    private readonly MenuContext menu = new MenuContext();

    public override void HandleInput(InputFrame input)
    {
        if (input[ControllerButton.Start].WasPressed)
        {
            Router.Push(menu);
            return;
        }
        // Read inside the handler so higher contexts' consumption is respected.
        if (attack.GetState(input).WasPressed) Attack();
        Move(input.State.Stick.X, input.State.Stick.Y);
    }
    public override void OnDeactivated() => StopPlayerActions();
    public override void OnDisconnected() => StopPlayerActions();
}

sealed class MenuContext : InputContext
{
    public override void HandleInput(InputFrame input)
    {
        if (input[ControllerButton.B].WasPressed) Router.Pop();
        // Handle menu navigation and selection here.
    }
}
```

`Attack`, `Move` and `StopPlayerActions` represent application logic.
`Push`, `Pop`, `Remove` and `Clear` may be called from handlers.
`Pop` removes the top context; `Remove(context)` removes that specific instance,
including one underneath a non-modal overlay. A structural change ends
that tick's routing immediately, so the newly exposed context cannot receive the
same press. `Clear` removes all contexts without briefly reactivating intermediate
screens. A context instance can belong to only one stack and cannot be pushed twice.
Capacity defaults to 16; exceeding it throws rather than allocating during a tick.

By default stack changes suppress all currently held physical buttons until they
are released. Both the held state and the eventual release edge are hidden;
a fresh press is required. Pass `suppressHeldButtons: false` to a stack operation
when intentionally preserving held controls. `Router.SuppressUntilRelease(mask)`
can suppress selected buttons. Sticks and analog triggers are not suppressed by
transitions; games decide whether movement should resume immediately.
Reconnection also suppresses already held buttons until release.

`OnActivated` runs when a context becomes eligible for routing; `OnDeactivated`
runs when it is removed or blocked by a modal context. Non-modal overlays leave
lower eligible contexts active. Cancel continuous gameplay actions in
`OnDeactivated`. `OnDisconnected` notifies every active context before dispatch,
including lower contexts under non-modal overlays. Disconnect does not generate
physical release edges. Lifecycle callbacks must not change the stack; do that
from `HandleInput` instead. Recursive dispatch is rejected.

## Consuming input and custom actions

Inside a non-modal handler, `input.Consume(ControllerButton.A)` hides A's held,
pressed and released states from later reads and lower handlers for this tick.
Masks such as `ControllerButton.A | ControllerButton.B` are supported by `Consume`
and the public `HeldButtons`, `PressedButtons`, `ReleasedButtons` snapshot properties.
The button indexer still accepts exactly one physical button.

`ConsumeStick(ControllerStick.Stick)`, `ConsumeLeftTrigger`,
`ConsumeRightTrigger` and `ConsumeAll` also support analog input. Consuming a
physical button does not consume the corresponding analog trigger travel.
Consumption is local to a router dispatch: it does not alter the global controller
cache, previously copied snapshots or another player's router. A handler owning a
held control should consume it every tick it owns it.

`InputFrame` is reused and must not be retained. `input.State` returns an immutable
copy of the remaining input. Existing `PlayerControls` can map that filtered input
with `controls.GetState(input)`; its parameterless overload reads the global cache
and therefore bypasses context consumption.

Use `ButtonBinding` for any custom action without extending the fixed
`PlayerBindings` action list:

```csharp
var attack = new ButtonBinding(ControllerButton.A);
// In a handler:
if (attack.GetState(input).WasPressed) Attack();
attack.Consume(input); // Prevent lower contexts from using the same physical button.
```

Each binding maps one physical button. Games own action names, duplicate-binding
policy, persistence, chords and any analog-to-button thresholds.

## Button assignment

Push a `ButtonCaptureContext` from the settings handler:

```csharp
var capture = new ButtonCaptureContext(attack);
Router.Push(capture);
// Later inspect capture.Status and capture.CapturedButton.
```

Capture blocks lower contexts, waits for all physical buttons to be released,
then assigns the next allowed fresh button press. It updates the supplied
`ButtonBinding` and pops itself. B cancels by default without modifying the binding;
disconnection also cancels. The result is `Waiting`, `Completed` or `Cancelled`.
After completion/cancellation, held buttons remain suppressed in the returning
context until release. A manually removed pending capture is cancelled.

Use `cancelButton: ControllerButton.None` to allow assigning B, or provide another
single cancel button. `allowedButtons` is a physical-button mask; the cancel button
is reserved even if included. Simultaneous allowed presses choose the lowest button
bit deterministically. Capture supports digital buttons; sticks/triggers need a
game-specific capture policy. Routers and contexts do not build any menu UI.

`ReadButtonsDown` remains a compatibility polling call and advances input itself;
avoid mixing it with snapshot reads within one game update.

## Validation

Managed context fixtures execute both on .NET and through AOT with forced GC and
ASan/UBSan. They cover modal/non-modal routing, consumption, lifecycle, transition
suppression, reconnection, capture success/cancellation and mutation guards.
Native fixtures cover transitions, connection changes, radial deadzones,
calibration, trigger ranges, timed-rumble backend contracts and cleanup. The actual GameCube backend is also
compiled against a recording SDK fixture to verify alarm creation/arming failures,
capabilities, native callback arguments, interrupt protection and alarm removal. Compiled
managed tests exercise nested value snapshots, bindings, validation, repeated
reads, GC stress and rejection of background input access. CI compiles the actual
libogc backend and checks 32-bit AOT execution. Physical device calibration and
alarm-driven motor expiry still need hardware verification.
