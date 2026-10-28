# High-level graphics: phase 2

Phase 2 adds reusable text/2D geometry, setup-time shapes and meshes, billboards,
mipmapped texture packages, and explicit scene captures. Existing methods and
DNTX v1 packages retain their signatures and defaults. All rendering remains on
the application executor; no additional worker threads are introduced.

## Retained geometry and shapes

```csharp
var builder = new Geometry2DBuilder(vertexCapacity: 2048);
builder.AddRoundedRectangle(new RectF(0, 0, 240, 80), 12, Color32.White);
builder.AddCircle(new Vector2(280, 40), 32, Color32.White);
using var panel = builder.Build();
// Inside an active Draw2D batch:
draw.DrawGeometry(panel, new Vector2(20, 20), Color32.White);
```

`Geometry2DBuilder` allocates bounded setup-time staging (3–65532 vertices).
Circle tessellation is 3–256 segments; rounded corners use 1–64 segments each.
Gradients interpolate four corner colors across two triangles. Build copies the
used vertices to an immutable native mesh; the builder can then be cleared/reused
or discarded. One builder uses either no texture or a single borrowed atlas.
`AddSprite` accepts texture regions and flips. Use separate geometry for different
atlases, or use an atlas white texel for solid shapes in textured geometry.

`Geometry2D` owns and disposes its native mesh, but **borrows its texture**. The
batch's current clip, camera, sampler, blend and effect apply at replay. Replay
flushes pending immediate geometry to preserve painter order and directly consumes
the native mesh vertices; it performs no managed vertex upload/tessellation.
Uniform-color geometry (including glyph layouts) reuses the indexed GX fetch path;
per-vertex gradients retain the direct path.
This is retained geometry, not a GX FIFO display list. The native bytes are
36 per vertex; Draws counts one replay, while Flushes counts queued batch flushes.
Geometry size is fixed at setup; use ordinary batches for frequently changing UI.

## Cached bitmap text

```csharp
using var title = new BitmapTextLayout(font, "PAUSED\nPRESS START",
    wrapWidth: 200, alignment: TextAlignment.Center, scale: 2);
// Each frame, inside the batch:
title.Draw(draw, new Vector2(60, 40), Color32.White);
```

The layout caches glyph positions/UVs as native retained geometry. It borrows the
font and atlas and owns its geometry. Dispose layouts before the font/texture.
Empty layouts allocate no native geometry. Size, LineCount, GlyphCount and
NativeBytes expose the setup result. Wrapping is deterministic fixed-cell wrapping
(including splitting words); zero width disables wrapping. Tabs advance four cells
and can wrap. CR is ignored and LF starts a new line; missing glyphs use the font's
existing fallback. Alignment uses the wrap width, or the longest line if unwrapped.
Text is bounded to 10922 BMP code units, at most 65532 vertices. This is not shaping,
kerning, proportional text, grapheme processing or automatic font streaming.

## Primitive meshes and billboards

`PrimitiveMeshes.Plane`, `Box` and `Sphere` return caller-owned indexed meshes with
normals and UV0. The plane faces +Y in XZ space. Sphere tessellation is bounded to
3–128 slices and 2–64 stacks; poles omit degenerate triangles. Construction belongs
outside frames. The meshes use existing materials, lighting, fog and queues.

`Billboard` owns one immutable quad and a reusable world matrix.
`renderer.DrawBillboard(billboard, camera, center, size, material)` faces the quad
toward the pass camera without changing vertices or allocating frame-time arrays.
Size is in world units. Supply the pass's camera and explicitly order translucent
billboards; they do not add an automatic particle sorter.

## Mipmapped DNTX v2

```sh
dnd-assets texture terrain.png terrain.dndtex --mipmaps
```

The existing converter call `DrawingPackages.Texture(image)` still emits v1.
The additive overload `Texture(image, mipmaps: true)` emits v2. It requires
power-of-two dimensions, maximum 1024, and creates a complete chain through 1x1.
Filtering averages premultiplied RGBA to avoid transparent color fringes, using
byte/sRGB-space RGB rather than gamma-correct filtering.

The 32-byte little-endian DNTX header keeps offsets 0 magic, 4 version, 8 GX
format, 12 width, 16 height and 20 total payload bytes. For v2, offset 24 is the
level count (1–11), and 28 remains reserved zero. Payload concatenates GX-tiled
levels with independently padded blocks; each dimension halves down to a minimum
of one. Runtime validates power-of-two dimensions, level count, exact byte length
and reserved fields before allocation. v1 keeps both reserved words zero.
The runtime also accepts a bounded prefix chain in v2. Palette formats remain
unsupported. `Texture2D.MipLevels` and NativeBytes report loaded storage.

Mip-0 behavior is unchanged. Mipmapped linear sampling uses trilinear filtering;
nearest uses nearest mip/texel. Material sampler overrides retain the mip chain.
The native `DndDrawTexture` descriptor now includes `levels`; custom native
backends must rebuild and initialize it (one for old textures). This descriptor
change is needed to communicate LOD state independently of texture format.

`ReplaceNative` replaces the entire chain; `ReplaceRgba` rejects mipmapped targets
because it only converts one level. Mips consume additional memory and texture
bandwidth; this addition improves minification quality and is not an unconditional
throughput claim. Existing v1 fonts and textures load unchanged.

## EFB capture and TEV post-processing

```csharp
using var capture = new SceneCapture(device, 320, 240);
// After scene.End3D(), inside the device frame:
capture.Capture(new Viewport(0, 0, 640, 480));
draw.Begin();
draw.SetEffect(postEffect); // Existing bounded TEV program API.
capture.Draw(draw, new RectF(0, 0, 640, 480), Color32.White);
draw.End();
```

SceneCapture owns its RGBA8 texture. Width/height must be positive multiples of
four. Capture runs between passes in an active frame. The source origin must be
even; source dimensions equal the target dimensions or exactly twice both target
dimensions (hardware half-size copy). `GraphicsDevice.CaptureToTexture` exposes the
same operation for a caller-owned texture. Targets cannot contain mip chains.

The operation copies the shared EFB without clearing it, issues pixel-mode sync,
waits for GX completion, and invalidates the texture cache before sampling. It is
an explicit synchronization/bandwidth cost. It does not introduce separate depth
buffers, arbitrary render targets, nested passes or concurrent rendering. Render
minimap content into an EFB region, capture it, and redraw/restore the main scene
as needed. Effect programs retain the existing GX/TEV and opacity limitations.

## Validation and performance acceptance

The native suite tests mip headers/lengths/v1 compatibility, retained geometry
preflight, clipping under downscaling and capture sizes. Debug/Release AOT with GC
stress tests cached layouts, wrapping/alignment, shape capacities, mesh sizes,
billboards, captures and disposal. Offline tests verify mip tiling, rectangular
chains, alpha-aware filtering and NPOT rejection.

The Dolphin graphics fixture retains all Phase 1 patches and adds retained shapes,
aligned text, half-size capture with TEV inversion, billboards and mip minification.
It compares immediate and cached text over 240 paired samples after 60 warmup
frames, alternating order. Timing windows include submission/flush, exclude setup,
capture synchronization and presentation, and use offscreen text to isolate CPU
submission. Neither these timings nor the older mesh/sprite windows measure full
frame or GPU time.

Telemetry rejects zero/saturated timings. Paired bulk/cached timings must stay
within 10% of their immediate counterpart. This relative budget is deliberately
wider than observed run-to-run rounding, independent of absolute host speed, and
is not a promise of a particular speedup. Physical hardware, full-scene frame-time
distributions and fill-rate/bandwidth workloads remain separate validation work.

A validated Dolphin run measured four-glyph immediate text at **35 µs** versus
**9 µs** for cached indexed geometry (864 native geometry bytes). The same run
measured 64 meshes at 282→222 µs and sprites at 369→329 µs. The 32x32 capture target
used 4096 native bytes. [Capture proof](measurements/graphics-phase-two.json)
records the DOL hash, 240 samples and 22 pixel patches. These measurements do not
establish hardware or full-frame performance.
