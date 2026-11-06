# Shared geometry and ordered passes

`MeshColorView` borrows a mesh's positions, normals, UVs and indices and owns
only an RGBA8 stream. Create and update it between frames:

```csharp
var gloss = new MeshColorView(transmission, reflectionColors);
transmission.SetVertices(vertices); // Both draws see these positions/UVs.
gloss.SetColors(reflectionColors);  // Four bytes per vertex, no geometry copy.
renderer.DrawMesh(transmission, transmissionMaterial, pose);
renderer.DrawMesh(gloss, glossMaterial, pose);
```

Colors must exactly match the geometry's vertex count. Views cannot be nested.
The native geometry allocation remains retained until its last color view is
released, including when its original managed owner has been disposed. A disposed
owner cannot receive new updates. The color-view allocation is reported through
its own `NativeBytes`; the owner's geometry remains accounted separately.
Varying colors use indexed GX arrays when unlit with identity material tint.
Lit, generated-UV and tinted cases use the existing direct fallback and apply
the view's colors to staged vertices. Indexed color-cache identity includes the
separate stream pointer so two views of the same geometry cannot reuse stale
cached colors.

`MeshPasses` prepares an ordered sequence of color/UV variants of one
uniform-color, unlit, single-texture mesh. It is useful when many transparent
samples must stay in order, such as a live backdrop blur:

```csharp
var passes = new MeshPasses(panelMesh, 81); // Between frames; prepares indices.
passes.Set(tap, weight, offset);            // Between frames; reuse the arrays.
renderer.DrawMeshPasses(passes, material, pose); // Inside a 3D pass.
```

Each pass **replaces** `Material.Color` and its primary UV transform. It does not
multiply either value. No caller material is mutated. Secondary textures,
lighting and generated coordinates are rejected. Effects must not require a
secondary texture. All variants and resources are preflighted before the first
draw. A malformed later variant therefore cannot submit a partial block.
Configure 1–256 passes. The geometry supports at most 65535 indices, in whole
triangles; color views cannot be pass-plan geometry. Mesh lifetime is borrowed,
so dispose it only after the plan is no longer used. Uniform vertex color is
multiplied by the per-pass color, using the existing integer tint convention.

The backend validates one material/world matrix, binds geometry once and replays
an aligned, immutable POS/UV index command list. It updates only the UV matrix
and uniform color between ordered GX draws. Index storage is budgeted and freed
with its mesh. Dynamic position/UV updates remain valid because topology cannot
change. Pass configuration, command preparation and geometry uploads are all
outside the active frame. Draw calls and triangles still count **every GPU
pass**; one native crossing is not one GPU draw and does not reduce fill work.
The backend restores the managed style and invalidates cached GPU material state
before the next ordinary draw.

Validation includes `mesh_streams_test` (allocation accounting, shared updates,
owner disposal, exact command bytes/padding and whole-block rejection),
`indexed_mesh_gx_test` (separate arrays, color/UV pass order and state restoration),
and native VideoMode DOL comparisons in the Samples repository. The integration
runs use the source SDK; no published package is overwritten. Cloud Dolphin
measurements establish comparative guest CPU cost, not physical console speed.
