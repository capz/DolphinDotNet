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
top-of-screen `DebugOverlay.WriteLine` log. Both the native GX demo and model renderer
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
* **BBA** appears after application network configuration is attempted. It shows
  `CONFIGURING`, `READY` or `CONFIG FAILED`, DHCP/static mode, and the configured IPv4
  address, netmask and gateway. Addresses appear after configuration succeeds.
  `READY` describes configuration, not a continuous physical link/cable test.
  Merely drawing the panel never initializes networking or starts DHCP.
  For the original Nintendo BBA, an additional `LINK UP`, `LINK DOWN` or `LINK ?`
  reports its physical Ethernet link. This read-only check runs every **2,000 ms**
  while the panel is visible, with an immediate sample when shown. Unsupported
  adapters, a busy EXI bus and failed reads show `LINK ?`; the check never waits
  for ownership of a busy bus. Configured addresses remain visible on cable loss.
  Physical link status does not establish Internet or server reachability.
* A build using the separate TCP GDB stub shows **BBA: DEBUG TCP CONFIG - STATIC**
  with that stub's configured addresses. This does not imply that the application
  socket stack is active or that a debugger is connected. Dolphin's host GDB port
  (`127.0.0.1:2166`) is independent of the guest BBA network and is not shown as the
  guest's IP address.
* **DEBUG LAST** shows the last state reported by the DolphinDotNet adapter:
  `UNKNOWN`, `CONNECTED` or `DISCONNECTED`. Debug symbol builds include an optional,
  versioned eight-byte telemetry slot bound through the ELF address map. The adapter
  checks its signature and writes only its four-byte state word on attach/clean
  disconnect, and reads it back to verify that the write persisted. An older or
  non-reporting build shows `UNAVAILABLE`; other GDB clients do not report through
  this slot. Variable inspection remains read-only.
* **DEBUG LIVE** replaces that row when live status is configured in Rider.
  While the overlay is visible, the game sends one 20-byte UDP ping every
  **2,000 ms** through its already configured BBA network. The adapter replies
  only while its debug session is running and its transport remains connected. Valid replies show
  `CONNECTED`; six seconds without a valid reply show `UNREACHABLE`. Cable
  loss shows `UNREACHABLE` on the next link check. A clean adapter report shows
  `DISCONNECTED`. `CHECKING` and `WAITING FOR BBA` distinguish startup from loss.
  Unreachable describes the game-to-adapter path; it cannot distinguish a host
  crash from a blocked or broken network route.

The panel uses five rows without BBA configuration and nine rows with BBA details.
It keeps the existing visibility toggle and bottom-left placement. Network snapshots
refresh on the separate two-second connectivity timer, without socket I/O. Memory
statistics retain their four-times-per-second refresh. Debugger reports are read
on each redraw. A halted target cannot redraw its overlay, so the screen retains
its previous frame until execution resumes. A long pause gives the live monitor a
fresh verification period on resume. **DEBUG LAST is not a live connection
guarantee**; an abrupt loss can leave that fallback report as connected.

Rebuild the target Debug build and install the updated Rider plugin to enable
adapter reports. Maps without telemetry remain supported and cause no status writes.
For live status, open Rider's **DolphinDotNet Attach → Manage targets → Advanced
connection options**. Set **Live status computer IPv4** to an address the game can
reach on this computer, and **Live status UDP port** (default 2167; zero selects an
automatic port). Leave the address blank to disable live status. Allow this UDP
listener through the computer's firewall and configure a working guest BBA network
first. The host GDB address `127.0.0.1` is not automatically the address the guest
can reach. Dolphin's BBA setup and the guest's network are separate from its host
GDB listener.
For games that do not otherwise start networking, initialize it during game startup
with `await Dolphin.Network.NetworkRuntime.StartAsync()` (or its static-address
overload); see [network startup](network-runtime.md). The overlay and host-endpoint
setting do not initialize networking or start DHCP themselves.

This channel supports Dolphin host-GDB and USB Gecko sessions with application
BBA networking. The native BBA/TCP GDB stub owns a separate uIP stack and explicitly
declines this UDP channel; it keeps `DEBUG LAST` plus physical `LINK` status.
An unsupported or unconfigured live channel never silently claims live connectivity.

Hiding the overlay closes the diagnostic socket and stops pings, reads and link
polls. Showing it starts fresh verification. The responder is passive while hidden;
it never polls GDB or interrupts execution. Each visible check drains at most four
small replies and sends one ping. Replies must match the configured peer, session
token and outstanding sequence. Clock resets, stale replies and session changes
cannot extend an old lease. Diagnostic UDP calls preserve application network error
state. The optional 24-byte heartbeat configuration is ELF-bound separately from
the existing eight-byte report, so old maps/plugins retain the fallback behavior.

Memory readings refresh four times per second. Timing and formatting use fixed native
buffers, with no managed allocations or storage/DMA operations. APIs run on the application's
thread and require DolphinDotNet AOT, like the other platform diagnostics.

Portable tests verify timing, counter reset/saturation, toggle behavior, network/debugger text formatting,
color and panel placement at 480- and 576-line resolutions, plus the connectivity
timer's two-second boundary, hidden/show behavior and clock reset. A mocked EXI
test checks the BBA register/bits, adapter identity, contention and failure cleanup.
Heartbeat tests cover loss/recovery, cadence, hiding, paused targets, invalid/stale
messages, bounded receive work and cleanup. A real native C guest and the real .NET
adapter exchange UDP on loopback, detect an abruptly killed adapter, and recover
with a new session token. These host tests do not establish physical BBA routing.
CI also validates the managed
toggle through AOT and builds the GameCube backend. The real-Dolphin inspection
gate also reads the retained telemetry slot after the adapter releases its connection
and requires the disconnected state in guest RAM. Console visual and timer accuracy
acceptance still requires an emulator or real hardware.
