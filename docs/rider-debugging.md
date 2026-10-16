# Rider-first DAP attach increment (A5)

The repository now contains a .NET 8 adapter in `tools/DolphinDotNet.DebugAdapter`.
It speaks DAP over stdin/stdout and attaches over TCP to an **already-started,
stopped Dolphin Emulator GDB target**. There is no custom IDE UI in this increment.
Do not describe this as validated Rider or physical GameCube integration.

## Build and attach contract

```sh
dotnet build tools/DolphinDotNet.DebugAdapter -c Release
```

Configure a DAP host to execute `dotnet` with argument
`tools/DolphinDotNet.DebugAdapter/bin/Release/net8.0/DolphinDotNet.DebugAdapter.dll`
using absolute paths appropriate to your checkout. Its stdin/stdout must be reserved
for DAP. Example adapter-specific **attach arguments** (not a complete Rider config):

```json
{
  "host": "127.0.0.1",
  "port": 2166,
  "elf": "/artifacts/debug-stack/Game.elf",
  "sourceMap": "/artifacts/debug-stack/dnd-il-debug.json",
  "addressMap": "/artifacts/debug-stack/Game.dnd-addresses.json",
  "sourceFileMap": {
    "/__w/DolphinDotNet/DolphinDotNet": "/local/DolphinDotNet"
  }
}
```

All three artifacts must belong to the same build; the adapter checks their hashes
before connecting. Source mappings use the longest matching path-component prefix,
are case-sensitive and bidirectional. Use explicit mappings for container/CI paths;
ambiguous documents and unmapped lines remain unverified. DAP clients can request
native paths or file URIs and zero- or one-based lines/columns.

Sequence: `initialize`, `attach`, wait for `initialized`, `setBreakpoints`,
`configurationDone`, wait for `stopped` (entry), then `continue`. Attach never resumes
execution implicitly. Breakpoints can be changed while stopped. Requests to inspect
or change breakpoints while running fail explicitly; pause first.

Supported: source breakpoint replacement, one named all-stop execution context,
stack trace pagination, continue/pause, bounded Step Into/Over/Out and disconnect.
Frame IDs expire when execution resumes. Unmapped native frames retain their PCs;
ambiguous PCs have no invented source location. Partial unwind reasons appear as
console output. Scopes are empty; variable/evaluate requests are unsupported.
Stepping temporarily removes native breakpoint installations and checks desired
breakpoint addresses at each instruction, including deeper calls. Installations are
restored at the resulting stop. Step limits, ambiguous source and unavailable CFA
remain explicit stop descriptions, not a claim of successful source advancement.

Continue waits are not ordinary-command timeouts. Pause sends Ctrl-C while the same
RSP reader drains its stop response. Disconnect interrupts a running target, waits
up to three seconds for its reply, removes owned breakpoints and closes the connection.
It does not send a kill command or terminate the externally launched emulator. The
normal policy leaves the target stopped; failed cleanup closes the session and is
reported, rather than claiming reliable detach/reconnect support.

## Rider compatibility gate (A6, still pending)

First try an existing DAP host, pinned to an exact Rider and plugin version:

- LSP4IJ documents a generic DAP configuration and file mappings:
  https://github.com/redhat-developer/lsp4ij/blob/main/docs/dap/UserGuide.md
- Its current documented support matrix lists Pause as unsupported:
  https://github.com/redhat-developer/lsp4ij/blob/main/docs/dap/DAPSupport.md

This is a candidate feasibility route, not a verified Rider integration. Confirm
installation compatibility, `.cs` gutter breakpoint routing alongside Rider's normal
.NET debugger, adapter process startup, C# source navigation, managed stack frames,
Step Into/Over/Out, and Pause. Record the exact versions and a protocol transcript.
If the host cannot route C# breakpoints or Pause, stop that approach and scope a thin
Rider debugger bridge. A generic DAP success does not satisfy this Rider gate.
Any custom UI must have an approved mockup before implementation.

## Acceptance and boundaries

Host tests: `python3 -m unittest discover -s tests -p test_debug_adapter.py` after
building the adapter. They launch the actual adapter process against a fake TCP
RSP target and exercise fragmented UTF-8 frames, identity rejection, configuration
ordering, shared breakpoint ownership, source ambiguity, source path translation,
step breakpoint arbitration/restoration, stale frame IDs and pause/resume/disconnect.
The existing `tests/DebuggerTests` also verifies a Continue wait outlives the ordinary
command timeout and can still be cancelled.

Real-Dolphin CI invokes `scripts/dnd_dap_probe.py` through `scripts/dolphin_smoke.py
--dap-adapter ...`. It must prove two C# breakpoint locations, source Step Into,
pause, and the separate Inner -> Outer -> Main / Step Out / Step Over fixture,
through the adapter process. CI success must be checked before claiming these new
DAP paths validated. The smoke harness owns emulator launch and termination.

Out of scope: DAP launch/build orchestration, custom Rider packaging, VS/VS Code
extensions, physical BBA/USB Gecko (A4.4 remains deferred), locals/objects,
expressions, managed exception filters, async stacks, real thread enumeration,
reverse execution, advanced optimized frames and automatic reconnect. Core source
stepping retains A4.5's known same-line, recursion and performance limitations.

## A6 feasibility spike: existing DAP host

A5 emulator gate passed CI run [38934685398](https://github.com/capz/DolphinDotNet/actions/runs/38934685398)
on commit aa63a0499e25fe0c98ba31b6205d792218ee3b5f. The real-Dolphin logs show both
ManagedGameCube source breakpoints, DAP source stepping, pause, Inner -> Outer ->
Main, Step Out to Outer:14 and Step Over to Outer:15. Host protocol tests, all
platform build workflows and the managed/compiler/native checks also passed.
This is emulator/DAP validation only.

Upstream LSP4IJ source was inspected at commit `cbe50d3d9ff7eac9d0055ad7695bf0b1fc5fb4f0`
(version property 0.21.1-SNAPSHOT, not an installed/pinned Rider plugin release):

- [`DAPDebugProcess.java`](https://github.com/redhat-developer/lsp4ij/blob/cbe50d3d9ff7eac9d0055ad7695bf0b1fc5fb4f0/src/main/java/com/redhat/devtools/lsp4ij/dap/DAPDebugProcess.java)
  implements Step Into/Over/Out and resume, but has no pause override/handler.
- [`DAPSupport.md`](https://github.com/redhat-developer/lsp4ij/blob/cbe50d3d9ff7eac9d0055ad7695bf0b1fc5fb4f0/docs/dap/DAPSupport.md)
  explicitly marks the Pause request unsupported. Therefore this route does not
  currently meet the complete interactive acceptance criteria as documented.
- [`DAPBreakpointTypeBase.java`](https://github.com/redhat-developer/lsp4ij/blob/cbe50d3d9ff7eac9d0055ad7695bf0b1fc5fb4f0/src/main/java/com/redhat/devtools/lsp4ij/dap/breakpoints/DAPBreakpointTypeBase.java)
  delegates breakpoint placement to debuggable-file mappings; its own DAP
  breakpoint type is distinct from Rider's regular .NET breakpoint handling.
  C# gutter routing/coexistence remains an interactive test, not a proven failure.
- The upstream developer guide documents `debugAdapterServer` registration and
  dedicated breakpoint handlers. A registration alone does not implement Pause.

Decision: keep the shared .NET adapter and stdio contract. Do not advertise
LSP4IJ-only setup as a complete Rider release. The next increment is a thin Rider
bridge feasibility implementation (or an upstream host Pause fix), with a pinned
Rider SDK/plugin combination. Prove C# breakpoint routing and Pause first, then
source navigation and the existing emulator stack/stepping script. Reuse Rider's
native Debug window and controls. Before implementing any custom configuration
UI, present a Rider-style mockup for user approval. No interactive Rider session
or physical console test was performed in this spike.
