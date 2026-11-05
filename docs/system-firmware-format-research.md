# Original firmware format research

## Research inputs

On 2026-11-05, at the user's request, three 2 MiB research inputs were obtained
from RetroBIOS revision `a9772dd6c7f6824c43c4c0117cbee56c70da9369`.
They are stored outside this checkout. No firmware, extracted model, texture,
sample or capture payload belongs in commits or repository test fixtures.

| Revision | CRC32 | SHA-256 |
| --- | --- | --- |
| NTSC 1.0 | 6dac1f2a | 5b2f2617cc63f1861a1f4ae44bb135caf10e202b5b9b79271daaf60f8caaf37d |
| NTSC 1.2 | 86573808 | 7c833f95839c226699108b93ab6acbe772788bfa3f05132b8ced5e78f5bc9a26 |
| PAL 1.2 | ad1b7f16 | 85ffcbc834238eb24c87fce27ba7b438cc53a77db013a0a7c59c78e0af08cb84 |

All three lengths, CRC32, MD5 and SHA-1 values matched the
[libretro system catalog](https://github.com/libretro/libretro-database/blob/master/dat/System.dat).
The collection's directory labels are not a language detector: its JAP input is
NTSC 1.0, while the USA input is NTSC 1.2. Regional hardware/language selection
is a separate concern. Source collection:
[RetroBIOS Dolphin inventory](https://abdess.github.io/retrobios/emulators/dolphin/).

These are online research inputs, not physical-console measurements.

## Acquisition and textures

Retail unencrypted headers contain publisher/copyright text. Later revisions
include an NTSC/PAL revision suffix; the early input has no such suffix. The
original synthetic descriptive prefix was insufficient for real images. The
reader now recognizes retail headers while continuing to require independent
decoded-record evidence for automatic encoding detection. Header tests generate
fictional publisher/revision strings rather than copying a firmware header.

The production inventory tool, after the header correction, decoded 14 records
from each NTSC input and 31 from PAL. All six named texture profiles resolved in
all three inputs, including per-header-relative offsets for the three faces:

| Texture | Width | Height | BTI format |
| --- | --- | --- | --- |
| Environment | 128 | 128 | I8 |
| Tiles | 64 | 64 | I8 |
| LogoTop / LogoLeft / LogoRight | 84 | 84 | RGBA8 |
| Wordmark | 352 | 40 | I8 |

This establishes bounded extraction against those images. Native rendering and
original-sequence comparisons are separate acceptance checks.

## Model and audio observations under investigation

Each input contains nine J3D1bmd1 models. Their section families are INF1, VTX1,
EVP1, DRW1, JNT1, SHP1, MAT1 and TEX1. This observation does not establish that
each field shares its J3D2 semantics. Two models have stale declared file lengths
that are smaller than the sum of the bounded sections; section coverage must be
validated against the decoded record rather than trusting that stale field.
The production inspector now validates complete section coverage for all nine
models in all three inputs, including those stale size fields.

The NTSC 1.2 and PAL 1.2 startup WSYS descriptors report 174,067 samples per
channel at 32 kHz, using 97,920 encoded AFC bytes each. The provisional synthetic
profile incorrectly equated the last encoded frame's capacity (174,080 samples)
with the actual sample count. WSYS bank layouts differ: the early input uses
68-byte names and 36-byte descriptors, while later inputs use 112-byte names and
44-byte descriptors. The production inspector resolves these from bounded tables
and rejects ambiguous layouts. All three now enumerate twelve wave descriptors.
## Original startup archive association

The production catalog now supports the original numbered WSYS/adjacent AW
profile. A 32-byte wrapper precedes WSYS: its first two words and final three
words are zero, its type is 2, and its size/allocation words describe a bounded
32-byte-aligned allocation. The early compact image retains stale wrapper size
words; the validated WSYS extent determines the archive's aligned start.

Qualification additionally requires one named `ipl_0.aw` bank, twelve compatible
AFC entries, exact encoded-frame capacities, an ordered/disjoint 32-byte-packed
archive layout, complete ROM bounds, and exactly two matching unlooped startup
entries. The full archive must not overlap a cataloged compressed resource.
Different valid source candidates are rejected as ambiguous. Unused alignment
bytes between waves can contain nonzero build data and are not decoded.
These are relative layout rules, not fixed ROM addresses or sample signatures.

Automatic extraction succeeds in all three inputs. Their interleaved AFC packets
are identical (SHA-256
`f2c489994e7ffd0dcbb499808c4ab13b6d2967f4923ff0aac04d92b71ae53ee2`).
The existing native memory-source decoder produced exactly 174,067 stereo frames,
released all handles, and matched the independent GC-anim-Wii/wsyster reference
decoder byte for byte. Big-endian interleaved PCM SHA-256:
`ae3e194d404f0022daf33ec1448ae71bcbd0901d76db32619f545d96ba6d490c`.
All three inputs produced the same PCM. Decoded sample files and external
reference code remain outside Git. This is host decoding evidence, not physical
stereo-output verification.

Generated fixtures cover both bank layouts, stale wrappers, partial final AFC
frames, malformed/overlapping/ambiguous sources, cancellation, native ownership
and AOT Sound/Music playback. Original material/animation semantics remain under
investigation.

## Original model import qualification (Phase 5)

All nine models in each of NTSC 1.0, NTSC 1.2 and PAL 1.2 pass the native
J3D1 importer (27 imports). Original hierarchy, rigid matrices, attributes,
validated GX primitive topology and embedded textures use the existing model
pipeline. The MAT1 layout is separate from MAT3: 100-byte header, 152-byte
records and 52-byte precomputed texture matrices. Signed TEV registers and
independent RGB/alpha operations, channel controls, alpha comparison, blend,
cull, depth location/write, dither and normal/UV texture generation are retained.
Aligned non-power-of-two original textures retain their dimensions.

The qualified profile has one channel, one TEV stage and at most one texture
generator. Unknown stages, fog, NBT and PREV initializers fail explicitly.
No source display list is submitted to GX. New generated geometry tests record
production GX state, reject malformed tables/opcodes/truncation/budgets and
prove independent source/native lifetimes. The nine named models include the
menu cube, both menu icons and memory cube; bounded JNT1 identities select them
without ROM offsets. Metadata CLI schema 2 reports NativeImportRequired rather
than interpreting structural recognition as semantic validation.

Default lighting is a white headlight when the material requests lighting;
applications can supply existing scene lighting. Physical IPL light positions
and pixel-identical console rendering remain unverified. These are rendering
comparison limits, not geometry replacements. No ripped reference meshes are used.

## Evaluated geometry and animated opacity

Original rigid DRW1 bindings use the shared skin/palette path. Bounds for framing
must be measured after palette evaluation: the wordmark's evaluated Y range is
approximately -177.388 to -123.5, rather than its raw vertex-array bounds. The
startup placement accounts for that original joint offset.

Some original materials source alpha from signed TEV registers and use opaque
blend state. Material alpha alone therefore cannot animate all meshes. The
optional per-instance opacity control preserves RGB, updates material/TEV alpha
and selects sorted alpha blending while fading. Reset restores original state;
ordinary authored rendering keeps the imported program and raster settings.
Emulator captures caught both the framing and fade issues during acceptance.
