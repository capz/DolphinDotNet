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
| BMG messages | original Wind Waker reader plus generic variable INF1, MID1, CP1252/UTF-16BE/UTF-8 and escaped control tags | Shift-JIS codepage tables, FLW1/FLI1 conversation flow, game-specific tag interpretation and writing |
| JPA volume emission | bounded point/cube/sphere/cylinder/circle/line, deterministic approximations | torus, partial sweeps, inner radius, emitter rotations and distribution parity with Nintendo JParticle |
| JPA simulation | base lifetime, velocity/spread, drag and alpha | FLD1 forces, SSP1 children, ESP1 alpha/scale/rotation animations, keyframes, collision and spawn timing |
| GX particles | textured alpha billboards with depth ordering | alternate billboard axes, strips, special GX blend/TEV/indirect stages, optimized batching |
| J3D material animation | named BTP/BTK/BRK, explicit BVA shape-to-material mapping | true per-shape visibility, multiple texture coordinates, Maya matrices, indirect material stages and original TEV semantics |
| BTI formats | all 11 base formats, non-power-of-two source decoding, raw non-indexed mip chains, indexed CI4/CI8/CI14X2 mip chains converted into GX RGBA8 | wrap S/T, independent min/mag, LOD bias, anisotropy, native CI/TLUT and sampler fidelity |
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

## Generic BMG and indexed BTI extensions (PR #139)

The optional `BmgCatalog.Read(bytes)` reads big-endian `MESGbmg1`
`INF1`, `DAT1` and `MID1` (when supplied) without applying a
game-specific attribute structure. Entries provide `Index`, a 32-bit
`Id`, raw attribute bytes, raw encoded text, rendered `EscapedText` and
uninterpreted `ControlTag` records. Encoding 0 is treated as a legacy
single-byte profile; 1 supports CP1252; 2 supports strict UTF-16BE;
4 supports strict UTF-8. Encoding 3 preserves non-ASCII Shift-JIS bytes
as `\\xNN` escapes until game-specific code pages are available.

```csharp
var catalog = BmgCatalog.Read(bmgBytes);
foreach (var message in catalog.Entries)
{
    // Integer IDs originate from MID1 when available, otherwise the INF1 index.
    Console.WriteLine(message.Id + ": " + message.EscapedText);
}
```

For desktop inspection use:

```sh
dotnet run --project tools/DolphinDotNet.Assets -- gcft bmg messages.bmg
```

`BmgMessages.Read` remains unchanged for detailed Wind Waker INF1
message attributes. Generic tag decoding retains byte-exact tags but
**does not execute text effects or game-specific commands**.

`BtiTexture.CreateNativePackage` now preserves indexed source mipmaps
by expanding each CI4, CI8 or CI14X2 level into GX-tiled RGBA8 within a
single DNTX v2 allocation. The original indexed payload is not submitted
to the GX TLUT sampler. Non-power-of-two BTI dimensions are accepted by
the image reader; native mip packages still require power-of-two images,
so `Texture2D.FromBti` falls back to RGBA8 base level for NPOT files.

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
