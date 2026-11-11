# Streamable model materials

`dnd-assets model input.glb output.dndmodel --streamable-textures`
exports a normal DNDM geometry/animation package with **zero embedded images**,
independent DNLT assets, and `output.dndmodel.textures.json`. The default importer
continues to embed its textures. glTF/GLB and Assimp-converted inputs support this
mode; direct PSO GJ/XJ imports reject it explicitly.

Use `--streamable-format auto|rgba8|cmpr` and `--mip-levels` as appropriate
(see CLI help). Auto preserves smooth alpha with RGBA8. Full independent ladders
support up to eleven levels; a level cap authors a partial ladder. Geometry,
skin weights, node transforms, animations and source UVs retain their existing
conversion. Authored glTF UV transforms are baked once by the model importer;
consumers apply only the selected DNLT level's UV transform afterward.

The version 1 JSON sidecar contains the model filename and SHA256, texture
indices with content-addressed family names, relative DNLT paths and SHA256,
logical dimensions, native wrap modes (0 clamp, 1 repeat, 2 mirror) and linear
filtering. Materials retain stable imported IDs (0 default, source index + 1),
primary/secondary image indices (-1 absent), GX combiner (0 none, 1 multiply,
2 add), normalized RGBA, alpha mode bits (1 mask, 2 unlit, 4 blend), cutoff and
double-sided state. Identical texture bytes share the same family hash, while
sampler settings remain material state. Existing GX material compilation also
exports its generated secondary images.

`Renderer.DrawModelMaterialPart(instance, materialIndex, transform, material)`
draws only primitives belonging to that imported material, including their
current skinning and node animation. It borrows the supplied material and
textures. Resolve/pin them immediately before submission and keep them alive
through the final GPU fence. The source `Model` owner may be disposed while its
instance remains alive. The native call validates the material index and
performs no resource allocation. Submit opaque parts first and sort transparent
parts through the application's draw queue; primitive sorting within a part
does not sort transparency across separate calls.

Hypercube's sidecar packer generates material factories, so no JSON parser or
image decoding is needed on the console. The renderer API also permits other
engines to perform equivalent wiring.
