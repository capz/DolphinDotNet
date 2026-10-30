# GCFT/JSystem interoperability (single coordinated branch)

This feature borrows format research from [GameCube File Tools](https://github.com/LagoLunatic/GCFT)
and its [gclib](https://github.com/LagoLunatic/gclib) submodule. All new readers are
independent managed implementations; no Python runtime, GUI, proprietary game data,
or third-party rendering components are included.

## Scope and status

| Family | API or command | Implemented profile | Explicit limit |
| --- | --- | --- | --- |
| Binary | `GameCubeBinary` | bounded big-endian access and Yaz0 decode | Yay0 retained in existing native model importer; not available from this shared reader |
| Detection | `GameCubeFormatDetector.Detect` | magic-based GCM, RARC, Yaz0, Yay0, BMG, BFN, J3D, JPC, TPL; hinted BTI | DOL and most proprietary disc assets have no universal magic |
| Archives | `RarcArchive.Read/OpenAsync`, `Entries`, `ReadFile` | read-only RARC tree, raw/Yaz0 wrapper, decompression on demand for Yaz0 members | no zero-copy member streaming, raw archive limited to 16 MiB |
| Textures | `BtiTexture.Read`, `Texture2D.FromBti/LoadBtiAsync` | GX I4/I8/IA4/IA8/RGB565/RGB5A3/RGBA8/CI4/CI8/CI14X2/CMPR | base mip into native RGBA8; wrap/LOD/minification and palette indexing not passed to native sampler |
| Material animations | `J3dMaterialAnimation.Read` | J3D1 BTK, BRK, BTP, BVA: bounded track decode + frame sampling | no model-material binding; source TEV and indirect stages may be unsupported |
| Particles | `JpcArchive.Read/LoadAsync` | JPAC1-00 and JPAC2-10 particle IDs, chunk inventory, texture references, TEX1 embedded BTI extraction | no emitter simulation, physics, billboard renderer or original TEV fidelity |
| Messages | `BmgMessages.Read/LoadAsync` | Wind Waker MESGbmg1 INF1/DAT1 with preserved byte sequences and control escapes | not a generic multilingual BMG engine |
| Fonts | `BfnFont.Read` | BFN1 INF1 metrics, MAP1 mapping, WID1 character widths | GLY1 image rasterization and text layout not included |
| Desktop | `dnd-assets gcft` | inspect, RARC extract, deterministic folder-to-RARC pack, BTI-to-PNG | no write-back to retail GCM, no arbitrary in-place update |
| GCM, DOL | pre-existing storage/debugging facilities | existing native disc mounting and executable mapping | do not duplicate with GCFT desktop code |

The single-branch delivery does **not** claim that metadata-only particle or J3D
material readers provide complete original game playback. Full GX particle
rendering and applying BTK/BRK/BTP/BVA to imported model draw commands require
additional renderer work and retail fixture qualification; this is a deliberately
reviewable foundation, not a complete compatibility claim.

## Console usage

```csharp
using Dolphin.Formats;
using Dolphin.Graphics;

// Access a RARC member, e.g. an extracted disc archive on an SD card.
var archive = await RarcArchive.OpenAsync("sd:/assets/scene.arc");
byte[] bytes = archive.ReadFile("textures/sky.bti");
using var sky = Texture2D.FromBti(bytes);

// Or load a raw BTI file directly.
using var cursor = await Texture2D.LoadBtiAsync("sd:/assets/cursor.bti");

// Non-skeletal J3D tracks currently return data, not a model-side material override.
var animation = J3dMaterialAnimation.Read(archive.ReadFile("anim/scroll.btk"));
float[] uv = animation.SampleUv(0, 12.5f);

// JPC parsing exposes particles and image data, without promising GX playback.
var jpc = JpcArchive.Read(archive.ReadFile("particle/effects.jpc"));
foreach (var texture in jpc.Textures)
{
    var rgba = texture.Decode();
}
```

The shared parsers operate on bounded input `byte[]` and can run in desktop tools
or managed GameCube code. `Texture2D` construction requires the appropriate
GameCube graphics lifecycle. Read-only archive paths are **relative**, case-sensitive,
and do not permit `..` segments. Mount storage before async file loads.

## Desktop commands

```sh
dotnet run --project tools/DolphinDotNet.Assets -- gcft inspect scene.arc
dotnet run --project tools/DolphinDotNet.Assets -- gcft extract scene.arc ./scene
dotnet run --project tools/DolphinDotNet.Assets -- gcft pack ./scene scene-rebuilt.arc
dotnet run --project tools/DolphinDotNet.Assets -- gcft bti title.bti title.png
```

Packing builds a fresh, uncompressed, deterministic RARC; it does not modify
the original disc image. The archive writer rejects symlinks, long paths,
non-ASCII member names, excessive directory depth and 16-MiB budget overruns.
This is not a byte-preserving repacker for unknown allocation flags and padding.

## Validation and secure decoding

- Bounds are checked before table/string/payload reads. Yaz0 declares its
  decompressed length and rejects invalid backreferences or output larger than 16 MiB.
- RARC parser checks directory cycles, duplicate member paths, valid node
  targets, bounded member ranges and path traversal.
- J3D animation decoders validate key-table spans before interpolating.
- BMG preserves raw source bytes, and rejects truncated control packets.
- JPAC rejects bad texture references, repeated particle IDs and malformed chunks.
- `tests/GameCubeFormatsTests` uses synthetic positive and negative fixtures.
- `tests/gcft_cli_smoke.py` checks deterministic RARC pack/extract/pack.
- Assets CI runs both checks. Run `dotnet run --project tests/GameCubeFormatsTests`
  and `python3 tests/gcft_cli_smoke.py` for local format verification.
- Console GPU fidelity, hardware performance and compatibility with individual
  retail discs are **not established** by these synthetic checks.

## GCFT and gclib license

GCFT is MIT-licensed (Copyright (c) 2020 LagoLunatic), and gclib is MIT-licensed
(Copyright (c) 2018 LagoLunatic). Their format documentation, record layouts and
algorithm descriptions informed the independent code. If code is subsequently
copied or translated, preserve the appropriate original MIT license/copyright
notice in the redistributed source. Do not assume that optional PyJ3DUltra or
other dependencies carry the same license without a separate review.

Sources examined: `gclib/{rarc,bti,bmg,bfn,jpc,jpa,j3d,animation}.py` and
`gclib/j3d_chunks/{ttk1,trk1,tpt1,vaf1}.py`, pinned in GCFT to
`4e8f48000b757defc05179e341c3a2b06dfaadee`. No commercial disc images,
fonts, textures or game assets are included.
