# System firmware resources

The public API is `Dolphin.System`. The assets originate in the GameCube IPL;
applications supply a console ROM or an explicit user-owned dump. No Nintendo
firmware, textures, geometry, fonts or sound samples are embedded in this library.

## Acquisition and lifetime

`SystemFirmware.ReadConsole(token)` performs read-only EXI transactions.
Each bounded 4 KiB request reads aligned 1 KiB ROM pages through continuous DMA,
with a new chip-select/address command for each page, matching libogc's ROM reader.
Unaligned caller ranges are copied from an internal, aligned page buffer; DMA
never targets managed/caller storage. The 20 ms deadline covers the complete
request, including page transitions, and a poll cap also bounds a stopped clock.
After a DMA timeout, its static buffer remains allocated and is quarantined until
process exit. Further firmware reads fail instead of reusing a possibly active
destination. Successful reads clear and flush the internal page buffer.
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
lengths and kinds, including both unencrypted font records. Those font records
are excluded only from encoding evidence. `InspectModel(index)` reports bounded J3D1 chunk metadata;
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

## IPL menu audio

`TurnSide`, `TurnCenter`, `Select`, `Back`, `OptionCancel`, `OptionConfirm`,
`OptionMove1`, `OptionMove2`, `OptionMove3` and `OptionSelect` reproduce the
original cube and option sequences on the qualified NTSC 1.2 / PAL 1.2
profile. They preserve instrument samples, tuning, bends, envelopes, layering,
panning and rotation's FIR feedback tail. These are prepared stereo PCM16 effects,
returned as normal caller-owned `Sound` resources. Unknown profiles, early NTSC's
different DSP setup and `PressStart` remain unavailable. See the
[evidence and playback limits](system-menu-audio-research.md).

Create the catalog with
`image.CreateCatalog(FirmwareCatalogOptions.IncludeMenuAudio, cancellationToken)`
before disposing the image. Both arguments are required to preserve the existing
`CreateCatalog(default)` call. Unknown option bits and sound values are errors.
`TryGetMenuSoundInfo(sound, out info)` is cached metadata only; it does not expand
audio or allocate a native voice. Returned metadata describes the complete output
rate/frame count, channels, profile and direct/prepared status. No raw event ID or
wave index is a public semantic identity. Without the option, queries return
false and creation explains that retention was not requested. Disposed queries
and creation throw `ObjectDisposedException`.

Use the following preparation and playback lifecycle:

```csharp
// On the preparation executor; image acquisition uses the existing API.
Sound[] PrepareMenuSounds(FirmwareImage image, CancellationToken token)
{
    var names = new[] { SystemMenuSound.TurnSide, SystemMenuSound.TurnCenter,
        SystemMenuSound.Select, SystemMenuSound.Back, SystemMenuSound.OptionCancel,
        SystemMenuSound.OptionConfirm, SystemMenuSound.OptionMove1,
        SystemMenuSound.OptionMove2, SystemMenuSound.OptionMove3,
        SystemMenuSound.OptionSelect };
    var sounds = new Sound[names.Length];
    try
    {
        using var catalog = image.CreateCatalog(FirmwareCatalogOptions.IncludeMenuAudio, token);
        for (int i = 0; i < names.Length; i++)
            sounds[i] = SystemResources.CreateMenuSound(catalog, names[i], token);
        return sounds; // Each successful Sound owns its native source independently.
    }
    catch { foreach (var sound in sounds) sound?.Dispose(); throw; }
}
// Dispose the caller-owned image after preparation and publish the sounds
// to the application executor. On input, retain sounds[0].Play()'s returned
// SoundChannel; reap/dispose it after IsPlaying becomes false. At shutdown,
// stop/dispose outstanding channels before disposing the sounds.
```

Load only needed effects: native audio has 16 clip slots, shared with other Sound
and Music resources. The default 8 MiB native budget is a ceiling. Unknown profiles
retain zero additional encoded menu sample bytes; `MenuAudioRetainedBytes` reports
that payload counter, excluding managed object overhead. There is no hidden ROM
reopen or decoded-PCM menu cache. Catalog access/disposal remains serialized.
Full preparation peak includes the image, ordinary catalog, source snapshots and
native validation scratch, not just `Sound.NativeBytes`.
The qualified catalog retains 77,984 bytes of additional encoded samples/recipe
data, including one shared copy of the firmware's resampling coefficients.
Rotation clips contain 74,480 stereo frames (2.3275 seconds at 32 kHz), including
a one-second bounded feedback tail after release; Select contains 38,640 frames
(1.2075 seconds). The additional Back, OptionCancel, OptionConfirm, OptionMove1,
OptionMove2, OptionMove3 and OptionSelect effects contain 47,200, 78,800, 39,600,
20,080, 20,080, 20,080 and 30,000 stereo frames respectively. `OptionMove1`
changes a value, `OptionMove2` selects a field within an editor, and
`OptionMove3` selects a row. Preparation uses cancellable decode/render slices. Each clip
owns PCM16 independently after the image and catalog are disposed; no native
voice is allocated until `Play`. The default native budget remains 8 MiB.

The opt-in metadata command is `firmware inspect INPUT --menu-audio`; it reports
bank-local and flattened positions, `waveId`, `baseKey`, source rates/frames/loops, archive-relative
extents, association evidence and unavailable reasons. The separate
`firmware audition INPUT WSYS_INDEX FLAT_WAVE_INDEX OUTPUT_DIRECTORY` research
command explicitly isolates one raw wave to a private directory. It does not
establish a named mapping. Never commit or upload its proprietary outputs.

`FirmwareWave.WaveId` is the original bank-qualified C-DF identity; `-1` means
the standalone WSYS has no ID table. Present tables are bounded and reject
duplicate IDs within a bank, invalid scene links and truncated records. IDs
can repeat across different banks and need not match flattened positions.
`BaseKey` reports the source tuning reference. `SystemMenuSoundInfo` adds
`SourceWaveCount` and `GetSourceWaveId(index)` to identify the waves used by a
complete effect without decoding or allocating a voice. Metadata remains
immutable after catalog disposal. The versioned semantic profile is
`ipl-menu-bms-v2`; public enum values and the existing three effects are retained.

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
Unseen material profiles fail explicitly. RGB-only effect overrides on MAT1 are
unsupported; use DrawModelWithMaterial to explicitly replace the complete material.
Structural inspection alone does not
validate geometry; the native import is authoritative. See
[format qualification](system-firmware-format-research.md) for tested profiles
and unverified lighting/render-comparison semantics.

## Optional startup rendition

`SystemStartupSequence` is an elapsed-time controller with `Update`, `Seek` and
`Restart`. It has no graphics, audio, storage or boot/reset side effects. The
same elapsed time selects the same curves at 50/60 Hz, including after a seek.

Prepare `SystemStartupRenderer.Load(catalog, token)` between application frames.
It uses five original startup models, their embedded textures and the extracted
tile texture through the existing `Renderer`/GX pipeline. It retains no catalog.
Use `Update(deltaSeconds)` or `Seek(seconds)` between frames, then call `Draw()`
inside a device frame. It owns its models, instances, tile mesh and texture;
dispose between frames. `NativeBytes` reports per-resource native budget charges; shared model textures
can be counted more than once. It is not a peak-memory measurement.
Start an ordinary startup `Sound` explicitly when playback is desired.

```csharp
using var startup = SystemStartupRenderer.Load(catalog, cancellationToken);
catalog.Dispose();
startup.Seek(4.2); // Deterministic letter-stage capture.
device.BeginFrame(Color32.Black);
startup.Draw();
device.EndFrame();
```

The rendition includes drop/rolling-cube motion, fifteen fading tiles, translucent
cubes, camera movement, normal-projected environment materials, the original
three-face logo and joint-driven wordmark reveal, hold and fade. Animation curves,
tile layout, camera framing and headlight are reconstructed. They are not original
firmware tracks or a claim of pixel-identical physical IPL behavior. Original
geometry and authored MAT1 programs are imported; no ripped Wii meshes are used.
`ModelInstance.SetNodeOffset`, `SetMaterialColor`, `SetTevColor` and `SetOpacity` provide bounded,
private native controls; reset methods restore the sampled/authored values.
`SetOpacity` preserves RGB, uses alpha-only GX stages without changing authored TEV registers, and
clamps originally unclamped alpha before compositing, and uses sorted alpha blending below full opacity even for an authored opaque mesh.
The opacity profile requires PREV outputs and unconditional original alpha comparison;
unsupported controls fail explicitly. Startup preparation rejects the background executor before any blocking import;
use the application executor between frames.
Physical hardware validation has not been performed. See [status](system-firmware-resources-status.md).

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
`FIRMWARE HARDWARE FAIL`. A continues through nine original model views and seven fixed animation times.
X toggles elapsed-time animation in those views; the fixed snapshots remain
independent of display rate until playback is enabled.

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
