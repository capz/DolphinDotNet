# Graphics APIs

The implementation has two layers over native libogc2 GX. `Dolphin.Graphics.Raw`
preserves C function names, constants and explicit state. `Dolphin.Graphics`
provides owned resources and ordered drawing passes. Both use one device and
presentation path; OpenGX is no longer required.

## Frame and pass ownership

```csharp
var device = GraphicsDevice.Default;
var renderer = new Renderer(device);
using var draw = new Draw2D(device);
// Create camera, transform, mesh and material during setup.
device.BeginFrame(Color32.Black);
renderer.Begin3D(camera);
renderer.DrawMesh(mesh, material, transform);
renderer.End3D();
device.BeginRaw();
// GX.GX_* commands. End every primitive and display list before leaving.
device.EndRaw();
draw.Begin();
draw.FillRectangle(new RectF(8, 8, 120, 24), Color32.White);
draw.End();
device.EndFrame();
```

Only one pass can be active. Complete each pass before changing viewport,
copying the EFB, entering another pass, or presenting. Calls execute on the
application executor. Do not await between a raw begin/end pair: the executor
holds a critical region across raw primitives and display lists. Dispose resources
explicitly. Disposal during a frame invalidates the public handle immediately
and retains storage until presentation completes. Native addresses already
submitted to GX remain the caller's responsibility.

`Renderer.BeginFrame`/`EndFrame` remain convenience methods for a whole model
frame. `Begin3D`/`End3D` attach to a device frame. The compatibility
`ImmediateRenderer` also uses this device; it cannot nest inside another frame.
Shutdown aborts incomplete raw streams before releasing native storage.

## Raw GX, GU and VIDEO

The checked-in schema generates 351 commands and 678 constants from libogc2
headers. Run `python3 tools/gx-bindings/generate.py` to reproduce the bindings.
`GX`, `GU` and `VIDEO` retain C names; `GXConstants` contains numeric header
constants. GU C implementation aliases are exposed under their public GU names.

Pointers, matrices, arrays, callbacks and returned native objects use
`NativeAddress`. `NativeBuffer.Allocate` creates aligned, zeroed native memory;
`AllocateObject(NativeObjectKind.Texture)` uses the SDK's actual structure size.
`Write` accepts bytes, floats or 16-bit indices; offsets into native memory are
bytes, while source offsets and counts are array elements. Write between frames,
then call `Flush` before GPU reads. Invalidate before reading GPU-written memory.
Addresses from `Address` keep their buffer reachable and check disposal.
`UncachedAddress` and `UnsafeFromUInt64` are borrowed escape hatches: retain the
owner separately, obey cache rules, and never pass managed heap addresses.

`GXColor` and `GXColorS10` cross the native boundary by value. Other native
structures are deliberately opaque. Layouts in raw buffers follow the target ABI
(big-endian GameCube), not the desktop host ABI. An address has no length/type
metadata; raw commands cannot validate all pointer ranges or vertex descriptors.
The caller must provide exactly the attributes/count declared to GX. Native
callbacks are addresses of native functions; managed delegates are unsupported
in interrupt callbacks. Do not redirect GX ownership, replace FIFO/VIDEO callbacks,
or change video framebuffers inside a managed frame. A fully raw program can
initialize GX/VIDEO itself; after `GX_Init` it owns presentation and cannot start
managed device frames. Raw state is not a saved/restored state block: later
managed draws rebind their required state. Viewport and scissor are restored at
pass boundaries; Draw2D uses the full framebuffer and its own logical clipping.

## Managed resources and drawing

`Mesh.Create` copies typed vertex arrays and optional `ushort` triangle indices
into native storage. Supported layouts are position/color, position/color/UV,
and position/normal/color/UV. `DynamicMesh.SetVertices` validates the complete
update before committing it, between frames. `DrawPrimitives` submits triangles,
lines or points, at most 768 vertices per call. Meshes allow 65,535 vertices and
196,605 indices. The native resource table holds 512 resources with a 16 MiB
payload budget shared by buffers, textures, meshes and batches.

`Material` provides vertex tint, one texture, alpha cutoff, culling, depth
read/write, opaque/alpha/additive/premultiplied blending and unlit or fixed diffuse
lighting. This is the GX fixed-function pipeline, with a camera-space headlight;
programmable shaders, PBR and automatically sorted transparency are not provided.
Draw transparent objects in the appropriate order. Transforms/cameras retain the
existing column-major managed matrix convention.

`Texture2D.Create` takes straight RGBA bytes and packs tiled GX_RGBA8 once during
setup. `FromPackage`/`LoadAsync` load DNTX native texture packages. Dimensions are
1–1024. Native packages support I4, I8, IA4, IA8, RGB565, RGB5A3, RGBA8 and CMPR,
with one mip level and no palettes. Managed sampling is clamp with nearest or
linear filtering. Use raw GX for mip chains, palettes, texture regions or more
advanced TEV pipelines. `CopyToTexture` synchronously copies a same-size EFB
rectangle into an RGBA8 texture between passes; this is not an arbitrary render
target abstraction.

`Draw2D` allocates its scratch and native capacity at construction. It preserves
submission order and batches adjacent compatible shapes/glyphs/sprites. Texture,
blend, clipping changes and capacity exhaustion flush the batch. It supports
filled/outlined rectangles, thick lines, triangles, rotated/source-region sprites,
fixed-cell bitmap text and nine-slice panels. Glyphs use BMP character codes;
newline and tab are handled, without runtime shaping or rasterization. Logical
size can stretch to the framebuffer. Batch capacities range from 6 to 65,532
vertices. Drawing methods reuse scratch storage; no per-draw managed allocation
or delegate is required. `DrawCalls` and `Flushes` expose the last batch counts.

## Offline asset commands

```
dnd-assets texture INPUT.png OUTPUT.dndtex
dnd-assets font ATLAS.png OUTPUT.dndfont CELL_WIDTH CELL_HEIGHT [FIRST_CHARACTER] [CHARACTERS]
```

PNG/JPEG decoding stays in the desktop tool. Textures are padded with transparent
pixels to GX 4x4 tiles and packed as AR/GB planes. Fonts use a fixed-cell atlas
with consecutive characters, defaulting to 32–127. The converter writes RGBA8;
external tools may supply other supported DNTX formats.

Both package headers use **little-endian** 32-bit integers; native pixel payloads
retain GX byte order. DNTX is 32 bytes: ASCII `DNTX`, version=1, GX format, width,
height, payload byte count, two zero reserved words. Exact tiled payload size is
required. DNFT is 32 bytes: ASCII `DNFT`, version=1, cell width, cell height,
first character, character count, embedded DNTX length, zero reserved word;
then one complete DNTX package. Loaders reject truncated or inconsistent sizes.

## Validation and limits

`GraphicsSmoke` compiles normal C# calls through AOT and runs against a recording
native backend with stress GC. It covers mixed passes, array value layouts,
raw calls, adjacent batching and deferred release. CI also builds and links a
GameCube DOL against libogc2. Host recording cannot establish visual correctness,
GPU performance, lighting appearance or cache behavior on hardware. Validate
those with Dolphin and real hardware before relying on a graphics workload.
