# GameCube RenderWare and Smash Melee models

Criterion's middleware is **RenderWare**. Pixar RenderMan is unrelated. Melee is HAL's GameCube HSD/DAT format; Sora's Brawl is Wii and outside this loader's scope.

```csharp
using var rw = await Model.LoadRenderWareAsync("sd:/model.dff", "sd:/textures.txd", "sd:/motion.anm");
using var fighter = await Model.LoadMeleeAsync("sd:/fighter.dat#Fighter_joint", "sd:/fighterAJ.dat#0");
```

Both APIs use the existing background native importer, cancellation, budgets, native mesh/texture/skinning/animation rendering, and ARAM residency. No desktop conversion or source GX command execution is required.

RenderWare implements a single DFF clump, frame hierarchy, portable and GameCube-native geometry, triangle/strip conversion, vertex colors/UV0, HAnim and up to four skin influences, GameCube TXD textures (ten GX formats including CI4/CI8 palettes and mip-chain validation; CI14X2 is unsupported), and standard uncompressed type-1 ANM tracks. Native layouts that include a per-vertex matrix prefix require `nativeVertexMatrixIndices: true`; this is explicit rather than a retry heuristic. MatFX and differing material/texture samplers require material approximation. Unsupported pipelines, morph targets, BSP worlds, other platforms' native textures and compressed/vendor animation codecs are rejected.

Melee implements DAT/USD public joint roots, JOBJ/DOBJ/POBJ GX geometry, supported rigid and envelope skinning, embedded TOBJ/TLUT textures, FigaTree motion and AnimJoint/AOBJ/FOBJ trees. FOBJ scalar quantization and constant/linear/Hermite slope commands are decoded into native TRS tracks; default frame rate is 60. `#symbol` selects a public root. For concatenated AJ animation archives, `#N` selects a zero-based member. Constraints, instances, billboards, IK, non-unit joint/animated scale requiring HSD scale compensation, visibility/material/shape animation and unresolved extern symbols are rejected. Strict materials require one supported diffuse UV0 texture; approximation is opt-in.

These profiles cover known middleware structures, **not every game-specific file that uses the engine**. In particular Burnout 1/2 custom mesh containers have not been verified and are not accepted as DFF. Independent Burnout 2 research identifies `.rcf/.rcx`, `.pss`, `.tra`, `.grd` and frontend containers; a texture extraction script is insufficient evidence for a mesh or animation reader. Sonic Heroes uses RenderWare, but its archive and pipeline variants still require asset-level verification. No retail model or physical-console visual acceptance is claimed.

Original native fixtures cover portable/native/weighted DFF, explicit matrix prefixes, TXD/ANM, Melee rigid/envelope geometry, eleven GX texture formats, FigaTree/AnimJoint/AJ motion, quantized FOBJ data, corrupt input and budgets. Managed AOT smoke tests cover async loading, draw calls and ARAM restoration.

Research sources (independent readers; no external implementation copied):

- [RenderWareFile GameCube readers](https://github.com/igorseabra4/RenderWareFile/tree/7df8dee1a27ede0cf9164245214d7c9c3afbf99a).
- [librw HAnim and ANM](https://github.com/aap/librw/blob/18532c2e13efbc1aa43f0b00b4459831f9414de0/src/hanim.cpp).
- [DragonFF native GameCube layout](https://github.com/Parik27/DragonFF/blob/master/gtaLib/native_gc.py).
- [noclip SYSDOLPHIN](https://github.com/magcius/noclip.website/tree/fd973ff240233c63883750a557c62baf6ff2a6c7/src/SYSDOLPHIN) and Melee FigaTree reader at the same revision.
- [HSDLib](https://github.com/Ploaj/HSDLib/tree/85567e40797de3c820a55476eca54c704921a848).
- [Edness Burnout research scripts](https://github.com/EdnessP/scripts/tree/2ecf57078656f236c25640571ef301091da13197/burnout).
