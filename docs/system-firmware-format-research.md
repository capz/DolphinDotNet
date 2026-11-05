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

The NTSC 1.2 and PAL 1.2 startup WSYS descriptors report 174,067 samples per
channel at 32 kHz, using 97,920 encoded AFC bytes each. The provisional synthetic
profile incorrectly equated the last encoded frame's capacity (174,080 samples)
with the actual sample count. The earlier WSYS family also needs separate
qualification. Archive association and original material/animation semantics
remain active research; no right-channel magic or fixed ROM offset is accepted
as proof of an AW mapping.
