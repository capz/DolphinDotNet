# JSystem advanced runtime fidelity: supported features and remaining qualification

This page follows [GCFT interoperability](gcft-jsystem-integration.md),
[runtime playback](jsystem-runtime-playback.md), and
[the compatibility backlog](jsystem-remaining-gaps.md).
PR #140 is not advertised as *complete retail compatibility*. It strengthens
five previously incomplete areas, with strictly bounded behavior and fail-fast
checks instead of guessing Nintendo's undocumented game-specific effects.

## Shift-JIS: complete WHATWG JIS0208 mapping

`ShiftJisCodec.Decode(bytes)` supports ASCII, halfwidth katakana, all
7,724 assigned WHATWG JIS0208 entries including NEC/IBM extensions,
Windows private-use pointers and relevant single-byte extensions.
It uses an embedded CC0 WHATWG index and needs no OS-provided codepage,
making it suitable for GameCube AOT deployments.
Unassigned pointers and malformed/truncated lead+trail pairs throw
`InvalidDataException`. The generic `BmgCatalog` now renders
encoding 3 with this codec while retaining raw encoded bytes and control tags.

This is **decoding**, not automatic Japanese BFN layout or bidi/line-breaking,
nor a CP932 encoder.

## Native GX BTI sampler fidelity

`Texture2D.ConfigureSampler(wrapS,wrapT,minFilter,magFilter,lodBias)` applies
independent wrap axes (clamp, repeat, mirrored), all six GX minification
filters, independent nearest/linear magnification and a clamped signed LOD
bias on the resident texture. Native draw-resource cache comparisons include
the sampler fields. `Texture2D.FromBti` reads these settings from the BTI
header and configures the native texture outside frames.

Native DNTX supports indexed BTI mips converted to RGBA8, as added in #139.
Mip-capable min filters are valid only on textures with more than one mip.
The standard style override may still intentionally replace these settings.
Remaining gaps: GX anisotropy/edge LOD and raw indexed TLUT residency,
different min/max mip clamps per BTI, game-specific indirect stages.

## J3D per-mesh BVA visibility

`J3dAnimationPlayer.BindMeshVisibility(bva,sourceShape,importedMeshIndex)`
maps a source BVA track onto **one imported mesh**, independently of any
shared material. A per-mesh mask is checked in the native model draw
preflight and applied without mutating the shared model's materials.
The old `BindShapeVisibility` material-level API is retained.

The SHP1-to-imported-mesh mapping is caller-provided. Importers can split or
merge source shapes; inferring that mapping without retained source identity
would be incorrect. Multiple GX texture generators, complete Maya BTK
semantics, indirect material animation and all J3D TEV variants remain open.

## JPA extra and child billboard effects

`JpcExtraShapeProfile.Read(archive,id)` reads a **conservative JPAC2-10**
ESP1 subset with normalized scale and alpha life curves, and the existing
`JpcParticleEmitter` applies these modifiers to billboard dimensions and
opacity. Rotation, alpha sin waves, pivot and complex keyframed modes fail
explicitly.

`JpcChildShapeProfile.Read(archive,id)` reads unrotated JPAC2 SSP1 child
billboards with bounded lifetime, one-shot child bursts, Y gravity,
optional parent-velocity inheritance and per-child scaling. The
preallocated 60Hz simulator caps total parent + child particles at 256.
Unsupported special child textures and complex shape modes fail explicitly.

`JpcParticleField` adds caller-configured directional, radial-attractor and
Y-axis vortex forces to the simulator without frame allocations. This is
a *runtime field adapter*, not a complete decoder of the JPAC FLD1
layout: gclib's documented JPAC reader also leaves FLD1 decoding incomplete.
Unrecognized FLD1 chunks are still rejected rather than ignored.

`JpcParticleEmitter.ConfigureTev(existingGxEffect)` attaches an already
validated DolphinDotNet DNEF GX combiner to particle draws; `BlendMode`
exposes alpha, additive, premultiplied and opaque blend modes.
The TEV backend validates and binds the program and resets indirect stages,
but no arbitrary retail BSP1 material is automatically translated to
DNEF stages. In particular, dual texture indirect stages and authored
TEV alpha registers need more GX work.

## Verification matrix

- `dotnet run --project tests/GameCubeFormatsTests`: synthetic Shift-JIS,
  invalid byte streams, BMG decoding, SSP1 child spawning, ESP1 scale/alpha,
  field-force evaluation and legacy GCFT/J3D fixtures.
- `python3 tests/gcft_cli_smoke.py`: desktop archive/BFN/BMG integration.
- `python3 scripts/ci_test_suite.py assets` and
  `python3 scripts/ci_test_suite.py graphics`: shared format/native GX tests.
- Full CI additionally builds compiler, managed SDK, driver and GameCube target.

For accurate game compatibility, acquire *owned* retail fixtures and capture
Dolphin emulator plus physical GameCube texture/filter/animation frames.
Review texture matrices and TEV stage output against each fixture, confirm
JPA field flags and local positions, and measure draw bandwidth and particle
memory usage before calling any of these systems complete.

## Attribution

The character mapping is generated from
[WHATWG index-jis0208.txt](https://encoding.spec.whatwg.org/index-jis0208.txt)
(CC0). JPA and J3D format research uses MIT-licensed
[GCFT](https://github.com/LagoLunatic/GCFT) and
[gclib](https://github.com/LagoLunatic/gclib) as documented in the
interoperability baseline. No proprietary retail assets are included.
