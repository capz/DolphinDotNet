# Sunshine and Resident Evil 4: on-device loading

The GameCube editions use two unrelated asset pipelines. Super Mario Sunshine
uses Nintendo JSystem/J3D. Original GameCube Resident Evil 4 uses Capcom's custom
BIN models, standard Nintendo TPL textures and FCV motion data. Ubisoft published
the original Windows port; its assets are outside this implementation's scope.

Both importers run on the existing native background service executor and return
ordinary `Model` instances. No desktop conversion step is required. Mount the
volume before loading. The original coordinate system and units are retained.

```csharp
using var sunshine = await Model.LoadJ3dAsync(
    "sd:/assets/actor.arc#model.bmd",
    animationPath: "sd:/assets/actor.arc#walk.bck",
    animationFramesPerSecond: 30,
    allowMaterialApproximation: true);

using var leon = await Model.LoadResidentEvil4Async(
    "sd:/assets/body.bin", "sd:/assets/body.tpl");

using var instance = sunshine.CreateInstance();
instance.Animator.Play("Motion", loop: true);
instance.Update(deltaSeconds);
renderer.BeginFrame(camera);
renderer.DrawModel(instance, worldMatrix);
renderer.EndFrame();
```

`allowMaterialApproximation` is an explicit choice: the native renderer has a
diffuse texture/color material, whereas these games use programmable GX TEV
combiners, indirect textures, specular stages and game-specific lighting. A
successfully imported mesh does not imply reproduction of the game's shader.

## Verified formats and implemented profiles

| Game / data | Known formats | Implemented here |
|---|---|---|
| Sunshine geometry | J3D2 BMD3, related BDL4 | INF1 hierarchy/material assignment; VTX1 arrays; JNT1 names/TRS; DRW1 rigid matrices; EVP1 weighted envelopes; SHP1 display-list packets; MAT3; TEX1. BDL's MDL3 commands are bypassed in favor of decoded geometry/materials. |
| Sunshine textures | BTI headers and embedded TEX1 | Embedded textures, palettes, tiled GX texels, wrap modes and mipmaps. A standalone BTI is an image, not a model input. |
| Sunshine skeletal animation | BCK/ANK1; related BCA/ANF1 | One optional animation file, exposed as `Motion`; shared/separate Hermite tangents, independent axis keys, compressed rotations and full-frame BCA channels. |
| Sunshine other animation | BTK/TTK1, BTP/TPT1, BRK/TRK1, BPK/PAK1, BVA/VAF1 | Known texture-matrix, texture-selection, color and visibility animation families; not implemented by the native `Animator`. Passing them as skeletal animation fails. |
| Sunshine packaging | RARC `.arc`, Yaz0 `.szs`, Yay0 `.szp` | Raw/compressed model or animation, compressed archive, and compressed members. `#relative/member.bmd` selects an exact member path. Without a selector there must be one matching model/animation. |
| RE4 geometry | Custom BIN `ModelData`, versions 20010801 / 20030817 / 20030818 | Parts hierarchy, fixed-point positions/UVs, s8/s16 normals, GX indexed display lists, rigid parts and three-influence skinning; compact and extended weight palettes. |
| RE4 textures | Nintendo TPL / TEXPalette | Descriptor/image/palette tables, tiled texels, mipmaps and wrap modes. This is different from F-Zero GX's AV TPL container. |
| RE4 motion | FCV `MotionData`, SEQ timing/events; shape data | Optional **forward-kinematic skeletal FCV**; all nine documented float/s16/s8 value/tangent codecs. Hermite tangents use normalized-segment units. Single constant float-only keys are accepted. |
| RE4 packaging | DRS/DAS Hakase containers, DAT bodies, yz2-compressed DAS, named EVD/ETM entries | Raw extracted BIN/TPL/FCV, bare DAT body, and uncompressed DRS/DAS body. `#N` selects a zero-based entry; `#BIN`, `#TPL`, `#FCV` or no selector requires a unique matching tag. Compressed yz2, EVD, ETM, SMD and nested effect archives are not implemented. |

All eleven standard GX formats are decoded: I4, I8, IA4, IA8, RGB565, RGB5A3,
RGBA8, CI4, CI8, CI14X2 and CMPR. Indexed images support IA8, RGB565 and RGB5A3
palettes. The output is native tiled RGBA8; source display lists are treated as
untrusted data and are never sent to GX. Source dimensions must be powers of two
up to 1024. Dimensions below four are expanded to four; mip levels below four in
either dimension are range-checked and omitted to match the native texture API.
The native sampler approximates source mip filter variants with its existing
single filter toggle and mip behavior. A differing min/mag toggle, BTI LOD bias
or nonzero minimum LOD requires material approximation.

For an archive with one model, texture palette and compatible motion:

```csharp
using var model = await Model.LoadResidentEvil4Async(
    "sd:/assets/example.drs#0", "sd:/assets/example.drs#1",
    animationPath: "sd:/assets/example.drs#2",
    animationFramesPerSecond: 30,
    allowMaterialApproximation: true);
```

These indices are illustrative; use the actual entries in the source archive.
Multiple models or animation entries are never combined implicitly. An archive
containing executable REL data is read only for the selected data entries.

## Animation and rendering boundaries

Animation is baked on-device at 60 samples per second plus authored key times;
native tracks interpolate translation/scale linearly and rotations as
quaternions. Supply the source animation frame rate (default 30). Baking is an
approximation between samples, especially for very fast rotations. Source loop
and ping-pong metadata is not imported; playback looping is chosen by `Animator`.

J3D rigid and weighted packets may share a mesh. Each mesh receives a native
palette that distinguishes a joint's identity bind matrix from its envelope
inverse bind matrix. `0xFFFF` packet matrix carry-over is supported. Envelopes
over four influences and meshes needing more than 256 palette entries fail.
Shapes using camera-facing billboards fail. Ordinary TRS and unit-parent Maya
segment-scale compensation are supported; scaled compensation fails, including
when an animation would require it. NBT's normal vector is retained; its tangent
basis is not used. The three-index NBT3 profile is unsupported.

Strict J3D materials accept a checked single-stage diffuse TEV profile, material
or vertex color, UV0, supported alpha compare, culling and blend factors. Complex
TEV, lighting, fog, texture transforms and additional texture layers need
`allowMaterialApproximation: true`; the adapter keeps the first texture and
material/vertex color and maps supported blending to the renderer. Depth state
and unsupported effects follow native renderer behavior in approximation mode.

RE4's IK motion is **not local bone animation**. The game stores model-space
end-effector targets and solves chains before applying double-joint blends.
Such clips, attach-camera channels and models needing nonempty double-joint
blend tables during playback fail explicitly; importing their translation keys
as FK would deform limbs incorrectly. Static rest geometry remains loadable.
Facial shape animation, SEQ time remapping/events, game-driven texture animation,
dynamic attachments and gameplay pose adjustments are not implemented. This
does not claim complete Leon/character animation playback. Mismatched position
and normal skin weights also require explicit approximation. Complex BIN
material flags (bump, alpha/specular layers) require approximation; the retained
material uses the first diffuse texture and native lighting.

The shared native model limits still apply: 1024 nodes, 1024 imported skinned
meshes, 256 joints per mesh skin, four influences per vertex, bounded native
handles, and no hidden loading from drawing. Input and decompressed buffers are
capped at 16 MiB each; the default combined budget is 8 MiB, maximum 16 MiB.
Raw data, decompression, intermediate meshes, textures, baked motion, package
construction and publication overlap count against the same budget. Files under
the input cap may still exceed it. Unsupported data, ambiguity, malformed offsets
or insufficient memory produce a failed task with `IOException`.

These APIs use the same cancellation, gate-release, cache and ARAM contracts as
[the other on-device source loaders](model-source-import.md). Parsing runs with
the managed gate released; bounded storage reads and final publication reacquire
it. Cancellation is checked before and after the native import. It cannot abort
the middle of decompression or animation baking.

## Research sources and verification

Research checked on 2026-10-15. Layouts were independently implemented; no game
assets or code from the research projects are embedded in DolphinDotNet.

- [noclip J3D model, texture and animation readers](https://github.com/magcius/noclip.website/blob/fd973ff240233c63883750a557c62baf6ff2a6c7/src/Common/JSYSTEM/J3D/J3DLoader.ts),
  [BTI reader](https://github.com/magcius/noclip.website/blob/fd973ff240233c63883750a557c62baf6ff2a6c7/src/Common/JSYSTEM/JUTTexture.ts),
  [RARC reader](https://github.com/magcius/noclip.website/blob/fd973ff240233c63883750a557c62baf6ff2a6c7/src/Common/JSYSTEM/JKRArchive.ts),
  and [Sunshine scenes](https://github.com/magcius/noclip.website/blob/fd973ff240233c63883750a557c62baf6ff2a6c7/src/j3d/sms_scenes.ts).
- [RE4 GameCube model structures](https://github.com/adonis-singh/re4/blob/9b76ff003081d6ece8b838453fb8310743a972c7/include/model.h),
  [model initialization/relocation](https://github.com/adonis-singh/re4/blob/9b76ff003081d6ece8b838453fb8310743a972c7/src/game/model.cpp),
  [GX attribute and weight-palette setup](https://github.com/adonis-singh/re4/blob/9b76ff003081d6ece8b838453fb8310743a972c7/src/game/trans.cpp),
  and [motion decoding](https://github.com/adonis-singh/re4/blob/9b76ff003081d6ece8b838453fb8310743a972c7/src/game/motion.cpp).
- The decompilation project's [BIN layout notes](https://github.com/adonis-singh/re4/blob/9b76ff003081d6ece8b838453fb8310743a972c7/tools/motion/meshbin.py),
  [FCV layout and IK warning](https://github.com/adonis-singh/re4/blob/9b76ff003081d6ece8b838453fb8310743a972c7/tools/motion/fcv.py),
  and [archive formats](https://github.com/adonis-singh/re4/blob/9b76ff003081d6ece8b838453fb8310743a972c7/tools/motion/archive.py)
  distinguish GameCube assets from other releases and unrelated formats sharing
  the same extension.

Original synthetic fixtures exercise native decoding without desktop conversion:
J3D BMD/BDL, float/fixed arrays, five GX primitive families, rigid/envelope skinning,
packet carry-over, all eleven texel formats, three palette formats, BCK/BCA poses,
Yaz0/Yay0/RARC, RE4 BIN/TPL/FCV codecs, DAT/DRS selection, bad offsets and budgets.
Managed AOT fixtures exercise async continuations, cancellation, drawing and ARAM
restoration. This is format-level validation using synthetic assets, not a claim
of testing all retail assets or visual accuracy on GameCube hardware.
