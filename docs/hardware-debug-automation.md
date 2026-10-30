# Automated GameCube hardware frame and input regression tests

DolphinDotNet's GameCube **Debug** ELF exposes an opt-in debugger automation API through the existing GDB RSP transport (USB Gecko or TCP/BBA). No new EXI wire commands, IDE UI, console overlay or arbitrary game-memory patching are required.

## Build and connect

Build the GameCube game with Debug, `DndDebugSymbols=true` and a standard `DndGdbTransport=UsbGecko` (channel 0/1) or `DndGdbTransport=Tcp` (with the normal BBA IP/port settings). The GameCube debug-symbol build defines `DND_DEBUG_AUTOMATION` and exports two native ELF symbols. **Release builds omit the automation mailbox entirely.** Standalone native repository builds may opt in using `make DND_DEBUG_AUTOMATION=1`.

Start the already-built game on the console with its GDB stub and attach the runner directly. **Do not connect Rider or Visual Studio simultaneously.** The host owns one RSP transport/USB serial port at a time. The Python runner does not upload an ELF via Swiss or the separate launcher; use your normal launch flow first.

USB Gecko requires `pip install pyserial`, the correct USB virtual-serial-port device and the installed driver's baud rate. TCP works with Python's standard library.

## Scripted synthetic input and captures

Create `test-run.json`:

```json
[
  {"port": 0, "buttons": ["A", "START"], "stick": [80, 0],
   "triggers": [180, 0], "polls": 2, "frames": 2, "capture": "pressed.png"},
  {"port": 0, "buttons": [], "polls": 1, "frames": 1,
   "capture": "released.png"},
  {"port": 4, "clear": true, "frames": 1,
   "capture": "physical.png"}
]
```

On GameCube / USB Gecko:

```sh
python3 scripts/debug_hardware_automation.py --elf bin/Debug/net8.0/MyGame.elf \
  --serial COM5 --baud 115200 --script test-run.json --output captures/
```

On Dolphin GDB / BBA:

```sh
python3 scripts/debug_hardware_automation.py --elf bin/Debug/net8.0/MyGame.elf \
  --tcp 127.0.0.1:2166 --script test-run.json --output captures/
```

The runner uses `powerpc-eabi-nm` (or host `nm`) to locate `dnd_automation_mailbox` and `dnd_automation_frame_boundary`. Its `HardwareSession` Python API can be imported into other automated test tools. It uses standard GDB `m/M` memory commands, `Z0/z0` breakpoints and `s/c` stepping/continuation; there is no custom EXI protocol.

Each step injects a bounded GameCube PAD sample, advances a requested number of presented GX frames, checks the native acknowledgement and the input poll counter, and saves a full native-resolution PNG plus metadata in `results.json`. The JSON includes the raw-XFB SHA-256. Add a `sha256` property to a step to assert a golden reference; any mismatch fails the run.

## Timing and captured pixels

The 96-byte mailbox consists of naturally aligned, **big-endian 32-bit** fields. Only write it **while the target is halted**. The debugger writes command data first and `request_seq` last; the next `dnd_input_poll` validates and ACKs it. Inputs are independently overridden on ports 0–3, with automatic expiry. Invalid samples do not mutate active overrides.

`polls` counts calls to `Controller.Update()`; `frames` counts GX presentations and is **not automatically the same**. The game's loop must call `Controller.Update()` to consume injected samples. The final `dnd_automation_frame_boundary` breakpoint runs only after `GX_DrawDone`, `VIDEO_WaitVSync` and the renderer's framebuffer swap. The framebuffer therefore stays stable while paused. The native runtime exposes the uncached XFB pointer, resolution, stride and format; the debugger reads in bounded 512-byte chunks. No frame-sized native buffer or second GPU copy is allocated.

The XFB is packed Y0/U/Y1/V (YUV422); the host converts to RGB using BT.601 limited-range conversion. The PNG is a **native presented XFB** capture, not a high-resolution Dolphin upscale, EFB depth/alpha capture or an external HDMI photo. A 640×480 frame requires **614,400 bytes** of GDB memory reads, which can be slow over USB Gecko. The target remains halted after the run.

## Versioned wire layout

```c
/* include/dnd_debug_automation.h */
extern DndAutomationMailbox dnd_automation_mailbox;
void dnd_automation_frame_boundary(void);

/* Byte offsets (32-bit big-endian):
 * 00 magic "DNDA"; 04 version (1)
 * 08 request_seq [commit last]; 0c ack_seq; 10 status
 * 14 command (1 set, 2 clear); 18 port (4 means clear all)
 * 1c poll duration; 20 connected; 24 buttons
 * 28/2c stick X/Y; 30/34 C-stick X/Y (signed)
 * 38/3c analog triggers L/R
 * 40 input_poll_serial; 44 presented_frame_serial
 * 48 XFB uncached pointer; 4c width; 50 height
 * 54 stride bytes; 58 XFB byte count; 5c format (1=Y0 U Y1 V)
 */
```

## CI and hardware acceptance

Run `make -C tests debug_automation_test && tests/debug_automation_test` and `python3 -m unittest discover -s tests -p test_debug_hardware_automation.py`. The full native CI runs both and validates Debug/Release compilation.

**Hardware verification is still required** for physical USB Gecko RSP memory throughput, supported XFB addresses, frame stepping and screenshot color fidelity. Dolphin/emulated tests cannot establish those console-specific facts. Do not claim the physical test rig is verified until a complete live run succeeds.
