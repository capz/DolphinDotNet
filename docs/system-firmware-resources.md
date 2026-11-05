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
synthetically. Actual regional compatibility is unverified without real dumps.

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

Automatic `catalog.GetStartupAudio()` resolution currently requires a strict
validated embedded FST, named WSYS/AW mapping, compatible metadata and one unique
startup pair. The actual IPL file-table layout is unverified. Non-FST locations
are unsupported; the implementation does not guess from a right-channel sample
signature or fixed absolute ROM offsets.

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

## Remaining implementation

Original J3D1 geometry/material semantics and the startup renderer are not
implemented. The supplied Wii reference uses ripped geometry and cannot establish
those semantics. A user-supplied dump is needed for original-format research and
regional comparisons; no ripped mesh or flat approximation is substituted.
Physical hardware validation has not been performed. See
[the status and evidence table](system-firmware-resources-status.md).
