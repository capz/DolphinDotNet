# JSystem / GCFT format-fidelity backlog and acceptance

This is the live checklist built from [the baseline interoperability matrix](gcft-jsystem-integration.md)
(PR #136) and [the runtime-playback limitations](jsystem-runtime-playback.md)
(PR #137). It separates **format decoding**, **native GX playback** and
**per-retail-game visual correctness**, which are not interchangeable.

## Completed foundations

- GCM disc access and ELF/DOL debugging via DolphinDotNet's existing storage/debugger.
- Bounded RARC parsing, Yaz0 decompression, BTI image conversion, J3D model/BTK/BTP/BRK/BVA
  sampling, BMG/BFN metadata, JPAC1/JPAC2 particle and texture indexing (PR #136).
- Opt-in GX JPA point/billboard simulation/drawing, J3D animated material override,
  name-based material matching, and native BTI mip-chain uploads (PR #137).
- This follow-up branch: zero-copy, bounded, read-only RARC member streams;
  BFN1 GLY1 glyph-sheet decoding and desktop PNG extraction; basic axis-aligned
  cube/sphere/cylinder/circle/line JPA volume sampling with deterministic seeds.

## Compatibility table

| Format feature | Current level | Still needed |
| --- | --- | --- |
| RARC read/archive extraction | implemented, read-only; streams do not decompress | chunked decompression of Yaz0/Yay0 members without loading entire archives, nested archives, controlled editing |
| BFN font | glyph mapping, widths, GLY1 tile/sheet RGBA extraction | draw-time text layout, wrapping, kerning, Shift-JIS glyph shaping, renderer atlas caching |
| BMG messages | Wind Waker INF1/DAT1 profile | standard BMG entry-size variants, MID1 IDs, UTF-16BE/Shift-JIS text, game-specific control sequences |
| JPA volume emission | bounded point/cube/sphere/cylinder/circle/line, deterministic approximations | torus, partial sweeps, inner radius, emitter rotations and distribution parity with Nintendo JParticle |
| JPA simulation | base lifetime, velocity/spread, drag and alpha | FLD1 forces, SSP1 children, ESP1 alpha/scale/rotation animations, keyframes, collision and spawn timing |
| GX particles | textured alpha billboards with depth ordering | alternate billboard axes, strips, special GX blend/TEV/indirect stages, optimized batching |
| J3D material animation | named BTP/BTK/BRK, explicit BVA shape-to-material mapping | true per-shape visibility, multiple texture coordinates, Maya matrices, indirect material stages and original TEV semantics |
| BTI formats | 11 base formats; raw non-indexed native mip chains | CI4/CI8/CI14X2 mip chains, wrap S/T, independent min/mag, LOD bias, anisotropy and sampler fidelity |
| GCM writeback | read-only disc filesystem support | disc image rebuilding or patching; preservation of FST/BI2 and alignment |
| DOL/REL | existing DOL debugging, gclib research | REL relocation reader/patching where needed; debugging independent of format-loading |
| Validation | synthetic malformed-input, host/AOT/native/GameCube compile checks | legitimate owned-disc fixture corpus, actual hardware render captures and measured RAM/GX budgets |

**Important:** This follow-up implements **additional restricted formats**, not
a blanket statement that every commercial GameCube disc's assets can be
rendered. Nintendo published no universal texture/animation format across all
studios. Use magic-based detection and game-specific adapters when required.

## GLY1 example

```csharp
var sheets = BfnGlyphSheets.Read(fontBytes);
if (sheets.TryGetGlyph(glyphCode, out var rect))
{
    var rgba = sheets.CopyGlyph(glyphCode); // source cell (RGBA8)
    // The caller owns any GPU texture atlas and draw-time layout.
}
```

To inspect a font's sheets on a desktop:

```sh
dotnet run --project tools/DolphinDotNet.Assets -- gcft bfn game-font.bfn glyphs/
```

This creates `glyphs/sheet-000.png`, etc. It does not decode indexed BFN GLY1
palettes because GLY1 lacks the separate BTI palette interface. Unsupported
image formats are rejected explicitly.

## RARC stream example

```csharp
var archive = RarcArchive.Read(archiveBytes);
using var member = archive.OpenMemberStream("res/model.bmd");
// A bounded, seekable, read-only view; no second member-sized allocation.
```

Streams expose **raw compressed bytes** if a member is Yaz0; use
`archive.ReadFile(path, decompressYaz0: true)` to decode with the 16-MiB
allocation budget.

## JPAC volume constraints

The additional emitter volume profiles use source BEM1 `volume_type`,
`volume_size` and `emitter_scale` fields. Cube, sphere, cylinder, circle
and line spawning use deterministic geometric distributions, not a
reverse-engineered guarantee of Nintendo's exact particle trajectories.
Partial sweeps, inner radii, rotations and torus emitters are rejected for
non-point shapes. Non-billboard BSP1, child/field chunks, advanced blend and
material animators remain unsupported. The 256-particle cap and 60-Hz
fixed step remain unchanged.

## Verification

Run from the repository root:

```sh
dotnet run --project tests/GameCubeFormatsTests
python3 tests/gcft_cli_smoke.py
python3 scripts/ci_test_suite.py graphics
```

The PR CI also checks compiler, native host, SDK, and GameCube cross-build.
Passing those does not establish game-specific compatibility. Source format
references are [GCFT](https://github.com/LagoLunatic/GCFT) and
[gclib](https://github.com/LagoLunatic/gclib), MIT; see the baseline license
notes. No proprietary images, game discs, or fonts were added.
