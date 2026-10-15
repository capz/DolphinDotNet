# PSO Episode III on-device models

Episode III retains GJ/GVM/NMDM assets but also has PRS-compressed data, big-endian GameCube BML archives, pointer-rooted REL model and motion libraries, Ninja chunk geometry, and a separate `fmt2` stage layout. Extensions alone do not identify the payload.

```csharp
using var model = await Model.LoadPsoEpisode3Async(
    "sd:/npc.bml#npc.rel", motionPath: "sd:/motion.rel#0");
using var instance = model.CreateInstance();
instance.Animator.Play("Motion");
```

Loading, decompression and decoding happen on the background executor. The result uses the normal native model, animation, GX texture and ARAM APIs. Defaults: 30 motion frames/second, 16-byte Euler keys, 8 MiB native import budget (maximum 16 MiB). Eight-byte Euler keys require `eulerKeyBytes: 8`; the loader does not guess between layouts. Cancellation is checked before and after native import.

Implemented profiles:

- Raw `.prs` and GameCube BML model/motion members, including inline PRS-compressed GVM textures. BML requires a member name if several model members match. The first data block starts at 0x800 and subsequent blocks align to 32 bytes.
- Wrapped GJCM and NJCM hierarchies, optional texture lists; pointer-rooted REL node lists with bounded delta relocation validation. REL models load all roots by default; `#N` selects a zero-based root.
- Ninja position/normal/diffuse and indexed weighted vertex chunks, strip meshes with UVs, normals and supported material state. Weighted vertices support up to four contributions with a common bind position/normal. Cache-list references are interpreted as bounded data, never executed.
- GVM/GVR direct GX texture formats I4/I8/IA4/IA8/RGB565/RGB5A3/RGBA8/CMPR, converted to the existing native texture representation. Ninja materials retain point/bilinear filtering and clamp/repeat/mirror samplers, with separate native textures when sampler states differ.
- NMDM skeletal motion and absolute-pointer REL motion libraries, linear translation/scale, Euler or quaternion rotation. Multi-entry libraries require `#N`. Motion targets must match the model hierarchy.

`fmt2` stages, camera tracks, unsupported chunk attributes, spline motion, external pointer bases, ZXY node evaluation, corner-color strips and weighted contributions that cannot share the native bind representation are rejected. Ambient/specular/environment, second-texture and mip/anisotropic material approximations require `allowMaterialApproximation: true`. This is a verified subset, not a claim that every Episode III map or NPC loads. Retail assets and actual hardware visuals have not been tested.

Original synthetic fixtures exercise GJ, NJCM, REL, weighted meshes, motion selection, literal and overlapping-copy PRS, BML inline textures, corrupt pointers/indices, cycles, truncation and memory limits under sanitizers.

References (format research only; no game data or external reader implementation copied):

- [PSO Ultimate Importer specification](https://github.com/theanine3D/pso_ultimate_importer/wiki/Format-Specification), sections on Episode III REL models, motion libraries and incomplete `fmt2` research; importer revision `190b0039f096fc6916d6087c82a22479a0221911`.
- [newserv BML reader](https://github.com/fuzziqersoftware/newserv/blob/ce2273a8e48c5c6fec9e21d85c490028498d8682/src/BMLArchive.cc) and PRS compression reader at the same revision.
- [SA3D Ninja chunk structures](https://github.com/X-Hax/SA3D.Modeling/tree/6e48cd1678ff7ffc0f6752de64f6ccb7bc3ad12c/src/SA3D.Modeling/Mesh/Chunk), compared with the PSO specification for endian fields, weights, materials and strips.
