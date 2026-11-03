# Indexed rigid model submission

Static meshes with uniform vertex color can bind their resident DNMD position,
normal and UV arrays directly to GX using the package's 80-byte vertex stride.
The index loop sends 16-bit references rather than decoding, shading, packing
and emitting full attributes for every triangle corner. No expanded geometry
allocation or per-frame allocation is introduced. The existing material/TEV,
texture, alpha/depth, determinant-aware culling and submesh sorting remain active.

Eligibility is conservative: no skin, at most 65,536 vertices, one uniform color
within [0,1], no animated material channels and no custom scene options. Lit
meshes additionally need unit source normals and an orthogonal uniform-scale
modelview transform. Other cases retain the direct renderer. These conditions
are checked per mesh/transform, so unsupported geometry is never omitted.
Uniform color and normal-length metadata is computed during package validation
and rebuilt on restore. Native budget accounting includes the small per-mesh
metadata. The package is flushed from CPU cache before publication/restoration,
and each indexed submission invalidates GX's vertex cache. Resident package
lifetimes continue to be owned by models and referenced instances.

For the default headlight, GX calculates ambient plus directional diffuse
lighting. The inverse-transpose normal matrix has its common scale removed
once per draw, including mirrored transforms. Alpha is independent of lighting.
GX's 8-bit lighting can differ slightly from CPU floating-point shading; this
path is not advertised as pixel-identical. Explicit scene lighting, animated
channels, nonuniform colors/scales, shears and non-unit normals use the existing
path. Full managed material overrides also retain their existing implementation.

The native diagnostic API `dnd_model_indexed_path(-1)` queries the enabled mode;
`0` selects direct submission and `1` enables the eligible indexed path. Changing
mode during an active frame or passing other values returns a negative error.
This allows comparisons of identical geometry, materials, scene and camera in
one executable. It does not alter model data or instance state. A
`DND_INDEXED_MODELS=0` native compile definition forces the direct implementation.

Recording GX tests exercise the production code, actual indexed references,
material/alpha/UV preservation, mirrored uniform scale, all relevant fallback
conditions and allocation stability. Loader, ARAM restore, native AOT and GC
stress tests exercise the unchanged residency/lifetime contracts. Dolphin/real
GameCube measurements are still needed to establish performance; no hardware
frame-time claim follows from a host recording test.
