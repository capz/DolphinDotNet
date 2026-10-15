# Live 3D model loading and PSO binary import

DolphinDotNet offers two complementary model paths:

1. **Runtime:** `Model.LoadAsync("sd:/asset.dndmodel")` reads an optimized
   self-contained model package without blocking the game loop. New:
   `Model.FromPackage(byte[] package)` loads an already available package
   directly from owned bytes without any intermediate file;
   `await Model.LoadObjAsync("sd:/object.obj")` parses a static OBJ *on the
   GameCube* and returns a normal `Model`.
2. **Desktop conversion:** `dnd-assets model SOURCE OUTPUT.dndmodel` compiles
   FBX, OBJ, glTF/GLB and **PSO GameCube .gj / Blue Burst .xj** with bounded,
   validated geometry directly into the standard package used by the existing
   renderer, `Animator` and ARAM residency APIs.

```csharp
using var model = await Model.LoadObjAsync("sd:/models/prop.obj");
using var instance = model.CreateInstance();
// Use the usual Renderer.DrawModel and GameObject APIs, no special renderer.
```

```csharp
byte[] bytes = Dolphin.IO.File.ReadAllBytes("sd:/models/character.dndmodel");
using var model = Model.FromPackage(bytes);
```

The live OBJ parser supports positive/negative references, vertices, UVs,
vertex normals, triangles and simple polygon fan triangulation. It is **static
geometry only**: materials/MTL, texture images, object hierarchy, skinning and
animation are not imported at runtime. For those, convert offline. The live
OBJ input is capped at 2 MiB and 16,384 triangles. Models still obey the same
16 MiB native package ceiling and per-model memory budget. Parsing runs in a
scheduled application-thread task, not while drawing.

## Direct PSO importer (no Blender, Python or other external program)

```sh
dnd-assets model player.gj player.dndmodel
dnd-assets model enemy.xj enemy.dndmodel
# For geometries referencing external GVM/XVM textures that cannot be retained
# by this subset, explicitly opt in to flat-color approximation:
dnd-assets model player.gj player.dndmodel --allow-material-approximation
```

Both importers read original binary data, not an intermediary exporter:

- **.gj:** GameCube/Episode III GJCM hierarchy; big-endian node transforms,
  vertex attribute tables, vertex colors, UVs, and GX 0x90 triangle lists /
  0x98 triangle fans with independent 8-/16-bit indices for each attribute.
- **.xj:** Blue Burst NJCM hierarchy and fixed-layout vertex descriptors,
  including multiple overlapping attribute records (retaining earlier UVs),
  signed 16-bit strip indices, diffuse vertex colors and double-sided triangles.
- Both build the existing DNMD model sections, preserving the node hierarchy
  and producing renderable static geometry. They validate file ranges, pointers,
  indices, node cycles, counts and native limits rather than silently skipping
  malformed data.

**Current restrictions:** Texture archives (.gvm/.xvm/.pvm), Ninja motion
(.njm), BML/PRS bundles, .rel stages and DC .nj chunk variants are documented
formats but are **not yet supported by this importer**. Textured PSO materials
require the explicit approximation option; this drops textures and certain
material effects, leaving geometry, vertex colors and UVs. Animation clips
from PSO .njm are not imported, and no joint skin weighting is synthesized.
Do not interpret the existence of an extension in a PSO archive as proof that
all its structural variants are supported.

The PSO implementation follows the [byte-accurate PSO format
specification](https://github.com/theanine3D/pso_ultimate_importer/wiki/Format-Specification)
rather than executing that importer. This ensures the conversion workflow has
no Blender dependency.

FBX and desktop OBJ conversion through Assimp CLI 5.3 remains available; this
**does not** mean FBX authoring formats are parsed directly on the GameCube.
The CLI is an offline tool and is not linked into DOL binaries.

## Commonly encountered formats across GameCube titles

These are **families of formats**, not a universal GameCube filesystem or API:

| Family | Models | Textures | Animation | Examples |
|---|---|---|---|---|
| Nintendo J3D/JSYSTEM | BMD, BDL | BTI, TEX1 in BMD/BDL | BCK, BCA (bones); BTK, BTP, BRK (texture/material) | Mario Sunshine, Wind Waker, Twilight Princess, Mario Kart: Double Dash!! |
| HAL sysdolphin/HSD | DAT (JOBJ/DOBJ/POBJ), HSF in related toolchains | Textures contained in DAT/HSF, GPU GX encodings | Joint, material and shape animation data in HSD | Smash Bros. Melee, Kirby Air Ride, Pokémon Colosseum/XD, Gotcha Force |
| Sega Ninja/PSO | GJ/GJCM on GC; NJ on Dreamcast; XJ on Blue Burst; models in REL/BML | GVM/GVR, PVM/PVR, XVM/XVR, platform specific | NJM/NMDM motion and REL motion libraries | PSO Episode I & II and Episode III; other Ninja engine games use related rather than identical layouts |
| Criterion RenderWare | DFF/clump geometry (often packed in game archives) | TXD texture dictionaries | RenderWare animation chunks, plus game-specific formats | Sonic Heroes, Shadow the Hedgehog, SpongeBob SquarePants titles |
| GX-native surfaces | Not a model container | I4, I8, IA4, IA8, RGB565, RGB5A3, RGBA8, CI4/8/14, CMPR | Not applicable | Shared hardware texel formats across engines; underlying bytes differ in game-specific containers |

Sources:
- [J3D BMD/BDL format specification](https://github-wiki-see.page/m/Sage-of-Mirrors/SuperBMD/wiki/Format-Specification)
- [HAL DAT/sysdolphin games and assets](https://wiki.vg-resource.com/DAT_%28Hal_Laboratory%29)
- [PSO model and motion specification](https://github.com/theanine3D/pso_ultimate_importer/wiki/Format-Specification)
- [Sonic Heroes/Shadow RenderWare file format tools](https://github.com/ShadowTheHedgehogHacking)
- [Nintendo GX texture formats](https://github.com/ryanbevins/delfino)

**Planning priority:** J3D BMD/BDL + BTI/BCK for broad Nintendo coverage;
HSD/DAT for Melee/Kirby/Pokémon; then PSO GVM/NJM/REL/BML, which remain
separate from the smaller direct GJ/XJ geometry parsers.
