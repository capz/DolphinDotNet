# Platform roadmap

DolphinDotNet uses a Unity-like managed/native split and Godot-like subsystem drivers. Managed game code must not depend directly on libogc, GX, OpenGX, PAD, or BSD socket details.

## Implemented platform foundation

- [x] devkitPPC/libogc DOL target
- [x] OpenGX-backed graphics driver with double-buffered GX presentation
- [x] perspective 3D render pass
- [x] orthographic overlay pass
- [x] diagnostic console ring buffer rendered over 3D
- [x] four-channel GameCube PAD state snapshots
- [x] frame loop based on SYS_MainLoop
- [x] nonblocking UDP socket driver
- [x] managed Console output routed to the graphical diagnostic overlay
- [x] real .NET assembly -> DND bytecode compiler proof of concept

## Runtime/compiler work

- [ ] locals and arguments
- [ ] branch/control-flow lowering
- [ ] calls between managed methods
- [ ] class/type layout, fields and constructors
- [ ] managed arrays/string operations beyond runtime primitives
- [ ] native-call metadata instead of compiler special cases
- [ ] managed Game/Input/Graphics/Network facade assembly
- [ ] exception model
- [ ] tracing or mark/sweep GC with explicit native roots
- [ ] DND IR
- [ ] PowerPC AOT backend

## Graphics

The public API will remain backend-neutral. OpenGX is the bootstrap GameCube backend because it exposes a useful OpenGL-style subset over GX. Resource handles will be opaque so selected hot paths can later move to native GX without changing C# games.

Next graphics acceptance test: compile a managed Game subclass that rotates a textured mesh while the overlay reports FPS, heap use, controller state and network status.

## Input

PAD is scanned exactly once per frame. Managed reads consume the immutable frame snapshot. This makes button transitions deterministic and enables future input recording/replay.

Planned managed surface:

    GamePad pad = Input.GetGamePad(0);
    if (pad.A.WasPressed) ...
    position += pad.LeftStick * speed;

## Networking

The native layer owns libogc initialization and descriptors. Managed code receives opaque socket handles. Game-loop networking is nonblocking; blocking convenience APIs, if added, must run on an LWP worker rather than the render/update thread.

Order: UDP -> TCP -> hostname resolution -> selected System.Net.Sockets compatibility. HTTP/TLS are explicitly not 0.1 requirements.

## Diagnostic console

Console.Write/WriteLine feed a bounded native ring buffer. The overlay is rendered after the 3D pass using an orthographic projection and alpha blending. Y toggles the overlay in the current native demo. This avoids framebuffer-console mode interfering with 3D rendering.

## 0.1 definition

A normal C# project builds to a .NET assembly. DolphinDotNet compiles it to native/AOT-compatible program data and links a DOL. On Dolphin and real GameCube hardware the demo:

1. boots,
2. renders a textured 3D object,
3. responds to GameCube controller input,
4. sends/receives a UDP packet,
5. displays Console.WriteLine diagnostics over the 3D scene,
6. runs without a desktop CLR.

The bytecode VM remains as the reference/debug backend after the PowerPC AOT backend exists.
