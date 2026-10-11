# Debug overlay

Enable or hide the performance panel at any time from managed game code:

```csharp
using Dolphin.Diagnostics;

DebugOverlay.PerformanceVisible = true;
DebugOverlay.PerformanceVisible = !DebugOverlay.PerformanceVisible;
DebugOverlay.PerformanceVisible = false;
```

It is disabled by default. The panel uses light gray text on an opaque black background,
inset eight pixels from the bottom-left corner. It is independent of the existing
top-of-screen `DebugOverlay.WriteLine` log. Both the OpenGX demo and model renderer
draw it automatically during presentation.

* **FPS** averages the last 32 presentation intervals.
* **FRAME** is the latest completed presentation interval in milliseconds, including
  game work, rendering and the vertical-sync wait. The first two presentations establish
  timing; toggling the panel on resets the window rather than including time spent hidden.
* **TRIS** counts scene triangles submitted in the current frame, including repeated
  model instances and the demo triangle. Overlay glyphs and backgrounds are excluded.
  This is a submission count; GPU clipping, backface culling, depth rejection and
  off-screen geometry do not reduce it.
* **MAIN** shows committed MEM1 space against physical capacity, including program/static
  storage, the reserved managed heap, stacks, framebuffers and native allocations. Free
  space combines the remaining arena and allocator free blocks. It is not the managed GC's
  live-object size.
* **ARAM** shows committed space against internal ARAM capacity: the reserved lower
  region plus rounded live allocations, including pending disposal. It reads the existing
  ARAM service without initializing or resetting it. Before initialization it shows
  `NOT INITIALIZED`; usage of outside owners within the reserved region is not discoverable.

Memory readings refresh four times per second. Timing and formatting use fixed native
buffers, with no managed allocations or storage/DMA operations. APIs run on the application's
thread and require DolphinDotNet AOT, like the other platform diagnostics.

Portable tests verify timing, counter reset/saturation, toggle behavior, text formatting,
color and panel placement at 480- and 576-line resolutions. CI also validates the managed
toggle through AOT and builds the GameCube backend. Console visual and timer accuracy
acceptance still requires an emulator or real hardware.
