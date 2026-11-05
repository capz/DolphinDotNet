# System firmware resources

The public API is `Dolphin.System`. The assets originate in the GameCube IPL;
applications supply a console ROM or an explicit user-owned dump. No Nintendo
firmware, textures, geometry, fonts or sound samples are embedded in this library.

## Acquisition and lifetime

`SystemFirmware.ReadConsole(token)` performs read-only EXI transactions.
`SystemFirmware.Open(explicitFallbackPath, token)` tries console ROM first, then
only the supplied path if the console source is inaccessible, invalid or has an
unsupported encoding/header. It does not silently switch sources when a later
resource layout is unsupported. The caller mounts the fallback volume using the
existing storage API. There is no implicit SD filename search or temporary ROM
write. Desktop tools can use `FirmwareImage.Load(path)` directly.

`FirmwareImage.Source` reports `ConsoleRom`, `ExplicitFile` or `Memory`. An emulated
EXI bus is still `ConsoleRom`; this field does not certify physical hardware.
`Read(bytes)` copies caller data. Dumps must be exactly 2 MiB. Encoding detection
requires multiple fully decoded independent records in exactly one interpretation.
The encrypted interval excludes the firmware font region.

Run acquisition, catalog creation and decompression on the existing background
executor. Serialize catalog access and transfer ownership through the scheduler;
the metadata cache does not support concurrent mutation/disposal. Publish graphics
on the application executor between frames.

```csharp
// CPU/background work; fallback volume was mounted by the application.
using var image = SystemFirmware.Open("sd:/ipl.bin", cancellationToken);
using var catalog = image.CreateCatalog(cancellationToken);
var source = image.Source;
var revision = image.Revision;
image.Dispose(); // Catalog retains resources, never this complete ROM.

// After publication to the application executor, between frames:
using var environment = SystemResources.CreateTexture(catalog, SystemTexture.Environment);
// The application owns each native texture and disposes it normally.
```

A catalog owns bounded compressed Yay0 records and, when resolved, a compressed
stereo source. `Extract(index)` returns detached decoded bytes. Disposing the
catalog clears owned record/audio buffers and releases cached metadata. Extracted
native textures and sounds have independent lifetimes.

## Inventory, textures and fonts

Enumerate `catalog.Count` and `catalog[index]` for ROM offsets, compressed/decoded
lengths and kinds. `InspectModel(index)` reports bounded J3D1 chunk metadata;
it does **not** imply geometry rendering support. `GetTextures(index)` reports
validated BTI header positions, dimensions, formats and mip counts. Unknown or
ambiguous layouts throw rather than choosing a plausible slice.

`GetTexture(SystemTexture)` resolves Environment, Tiles, LogoTop, LogoLeft,
LogoRight and Wordmark using size/relative-position hints backed by model/TEX1
structure and complete BTI validation. Both 14- and 31-record families are tested
synthetically. All six profiles resolve in NTSC 1.0/1.2 and PAL 1.2 research inputs.

`SystemResources.CreateTexture` returns the existing `Texture2D`: native formats,
indexed palettes, alpha, mip conversion, sampler state and GPU cache/lifetime
rules are those of `Texture2D.FromBti`. Only immutable metadata is cached; native
texture objects are never shared implicitly. `SystemFont.Load` wraps the existing
`IplFont` implementation, which remains supported with its original API.

## Original stereo audio

`FirmwareWaveSystem.Inspect` validates WSYS metadata without duplicating a codec.
`FirmwareStereoAudio.Read(wsys, aw, leftIndex, rightIndex, token)` losslessly
interleaves compatible, non-overlapping original AFC channels into the existing
AFC container. It preserves sample count, sample rate and representable loops;
incompatible/unknown layouts fail explicitly.

Automatic `catalog.GetStartupAudio()` resolution supports the original numbered
WSYS/adjacent AW layout, qualified against NTSC 1.0, NTSC 1.2 and PAL 1.2. It
validates the record wrapper, both WSYS bank layouts, all twelve packed wave
extents, the archive name and one unique compatible startup pair. Alignment fill
between waves is not sample data and may be nonzero. A strict embedded FST/named
archive mapping remains available for explicit supported containers; an actual
FST is not required for the original images. No right-channel sample signature
or absolute ROM address is a locator.

The original startup metadata reports 174,067 samples per channel at 32 kHz;
the final AFC frame is only partly used. That exact count is preserved. `Layout`,
`FirmwareOffset` and `ArchiveBytes` report how the source was associated; explicit
standalone archives have no ROM offset. See the
[format evidence](system-firmware-format-research.md).

`Sound.FromEncoded` and `Music.FromEncoded` accept resident compressed data and
optional paired-bank data, copy it into native ownership, and reuse the existing
bounded codecs. No filesystem operations occur. Startup adapters are:

```csharp
using var sound = SystemResources.CreateStartupSound(catalog, cancellationToken);
// Validation may run on the background executor. Playback uses the application executor.
using var voice = sound.Play();
// Stop/dispose through the normal Sound/SoundChannel API.
```

A music adapter is also available. Native playback retains no managed array or
catalog pointer. The catalog/image may be disposed after a sound/music resource
has been created. Cancellation during validation disposes the native clip.

## Budgets and inspection tools

The ROM is 2 MiB. The catalog limits decoded records, retained compressed data,
scan work, resource counts, WSYS entries and TEX1 pixel work. Native EXI reads are
limited to 4 KiB, a 20 ms deadline and a finite poll count, with cleanup on failure.

Firmware-reachable GameCube bundles select an 8 MiB managed heap; ordinary apps
retain 256 KiB. Generated Makefiles accept `DND_MANAGED_HEAP_BYTES`, bounded to
256 KiB–16 MiB by the native runtime. Account for native graphics/audio allocations
alongside the managed heap in MEM1. Allocation failures remain errors.

For metadata-only diagnostics:

```sh
dotnet run --project tools/DolphinDotNet.Assets -c Release -- firmware inspect /path/to/ipl.bin
```

The command prints JSON inventory, structural model sections, texture resolution
results and audio metadata. It exports no firmware or proprietary asset bytes.

For deterministic emulator proof, stop at a completion symbol before reading
result buffers. The older Dolphin asynchronous interrupt path did not produce
reliable memory replies in this environment. `dolphin_smoke.py` supports
`--memory-after-symbol`; its memory probe handles bounded reads and unique LTO
private-symbol aliases. Completion/bytes must match; boot logs alone are insufficient.

## Original models

`catalog.GetModelIndex(SystemModel)` resolves nine startup/menu model identities.
`SystemResources.CreateModelAsync(catalog, name, token, nativeBudgetBytes)` extracts
an owned snapshot and imports it on the existing service executor. It returns
an ordinary `Model`; instances, transforms, rendering, ARAM and disposal follow
existing model rules. Image/catalog lifetimes are independent after extraction.
`Model.LoadJ3dAsync(byte[])` also supports an explicit owned buffer without files.

The J3D1/MAT1 profile preserves original topology, joints, attributes, embedded
textures, signed TEV colors, RGB/alpha operations, projection and raster state.
Unseen material profiles fail explicitly. Structural inspection alone does not
validate geometry; the native import is authoritative. See
[format qualification](system-firmware-format-research.md) for tested profiles
and unverified lighting/render-comparison semantics.

The startup controller is the next implementation phase. Physical hardware
validation has not been performed. See [status](system-firmware-resources-status.md).

## Console acceptance fixture

`tests/SystemFirmwareHardwareSmoke` contains no bundled firmware. The default
build reads only EXI, so an absent or invalid console source fails rather than
being hidden by file fallback. A second build with
`-p:DefineConstants=FIRMWARE_EXPLICIT_FILE` mounts SD2SP2 and reads the explicitly
selected `sd:/ipl.bin` test path, without trying EXI. Build each into a separate
output directory and retain its matching ELF/DOL. Use the repository's normal
GameCube build/launch configuration, with Debug automation enabled as described
in [hardware-debug-automation.md](hardware-debug-automation.md).

The fixture records source, revision and inventory counts in the existing debug
log, disposes the full image, then presents Environment on black. A cycles
through Tiles, LogoTop, LogoLeft, LogoRight and Wordmark. START creates/plays the
existing stereo Sound; B exits and disposes resources. Unknown layouts report
`FIRMWARE HARDWARE FAIL`. The fixture does not interpret models or animate them.

After launching on the console with the debugger stub, capture the sequence:

```sh
python3 scripts/debug_hardware_automation.py --elf /path/to/fixture.elf \
  --serial /dev/ttyUSB0 \
  --script tests/SystemFirmwareHardwareSmoke/captures.json \
  --output /tmp/firmware-console-captures
```

Use the matching transport/port for the actual rig. Captures contain original
texture pixels and must stay outside commits. Record the actual region/revision,
physical vs emulated target, source result and stereo output separately. The
frame runner verifies transport/frame acknowledgements, not audio fidelity or
texture identity: inspect the images against the same firmware's original IPL
output, listen to or record both audio channels, and preserve errors. Golden
hashes can be added locally after that comparison; none are fabricated here.
The 360-frame final step allows the nominal 5.44-second sound to finish at both
50 and 60 Hz during uninterrupted playback. Debugger capture pauses can interrupt
audible playback; use a separate uninterrupted run for sound comparison.
