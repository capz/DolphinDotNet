# On-device model loading

Original source assets can be loaded directly into DolphinDotNet's existing native
model renderer and Animator:

- `Model.LoadJ3dAsync`: Sunshine BMD/BDL, embedded BTI textures, BCK/BCA,
  Yaz0/Yay0 and RARC archives.
- `Model.LoadResidentEvil4Async`: GameCube BIN/TPL, compatible FK FCV motion,
  DAT bodies and uncompressed DRS/DAS archives. See [profiles and limits](gamecube-model-formats.md).
- `Model.LoadFZeroGxAsync`: F-Zero GX GMA/GCMF + AV TPL, LZSS and U8/ARC;
  see [supported profile](fzero-gx-loading.md).
- `Model.LoadPsoAsync`: GameCube GJ + GVM/GVR textures + one linear NMDM motion.
- `Model.LoadFbxAsync`: binary/ASCII FBX, supported images, linear skinning and
  baked animation stacks, parsed on the console.
- `Model.LoadObjAsync`: OBJ geometry, MTL, base-color images, polygon triangulation
  and generated normals. OBJ has no standard skeletal animation.
- `Model.LoadGlbAsync`: embedded GLB v2, PNG/JPEG, linear skins and core TRS clips.

The [on-device source loading guide](model-source-import.md) describes APIs,
format restrictions, native memory budgets, third-party pins and validation.
No desktop importer process or preconversion is required by these APIs.
`Model.LoadAsync` and `Model.FromPackage` continue to support compiled packages.
The desktop tool is unchanged by this implementation.

## Commonly encountered formats across GameCube titles

These are **families of formats**, not a universal GameCube filesystem or API:

| Family | Models | Textures | Animation | Examples |
|---|---|---|---|---|
| Nintendo J3D/JSYSTEM | BMD, BDL | BTI, TEX1 in BMD/BDL | BCK, BCA (bones); BTK, BTP, BRK (texture/material) | Mario Sunshine, Wind Waker, Twilight Princess, Mario Kart: Double Dash!! |
| Capcom RE4 GameCube | Custom BIN | Nintendo TPL | FCV bones/IK and shape data, SEQ timing/events | Resident Evil 4; other platform editions require separate verification |
| HAL sysdolphin/HSD | DAT (JOBJ/DOBJ/POBJ), HSF in related toolchains | Textures contained in DAT/HSF, GPU GX encodings | Joint, material and shape animation data in HSD | Smash Bros. Melee, Kirby Air Ride, Pokémon Colosseum/XD, Gotcha Force |
| Amusement Vision | GMA/GCMF | AV TPL | SKL/MTA/MAL and COLI scene animation; currently unsupported | F-Zero GX; related Super Monkey Ball variants require verification |
| Sega Ninja/PSO | GJ/GJCM on GC; NJ on Dreamcast; XJ on Blue Burst; models in REL/BML | GVM/GVR, PVM/PVR, XVM/XVR, platform specific | NJM/NMDM motion and REL motion libraries | PSO Episode I & II and Episode III; other Ninja engine games use related rather than identical layouts |
| Criterion RenderWare | DFF/clump geometry (often packed in game archives) | TXD texture dictionaries | RenderWare animation chunks, plus game-specific formats | Sonic Heroes, Shadow the Hedgehog, SpongeBob SquarePants titles |
| GX-native surfaces | Not a model container | I4, I8, IA4, IA8, RGB565, RGB5A3, RGBA8, CI4/8/14, CMPR | Not applicable | Shared hardware texel formats across engines; underlying bytes differ in game-specific containers |

Sources:
- [J3D BMD/BDL format specification](https://github-wiki-see.page/m/Sage-of-Mirrors/SuperBMD/wiki/Format-Specification)
- [HAL DAT/sysdolphin games and assets](https://wiki.vg-resource.com/DAT_%28Hal_Laboratory%29)
- [PSO model and motion specification](https://github.com/theanine3D/pso_ultimate_importer/wiki/Format-Specification)
- [Sonic Heroes/Shadow RenderWare file format tools](https://github.com/ShadowTheHedgehogHacking)
- [Nintendo GX texture formats](https://github.com/ryanbevins/delfino)

**Further coverage:** J3D material animation and RE4 IK/archive compression;
HSD/DAT for Melee/Kirby/Pokémon; expanded PSO NJ/REL/BML. The implemented
GJ/GVM/linear-NMDM profile does not cover every PSO variant.

GameCube additions: [RenderWare and Melee profiles](renderware-melee-loading.md), and [PSO Episode III PRS/BML/REL/Ninja profiles](pso-episode3-loading.md). These use `LoadRenderWareAsync`, `LoadMeleeAsync`, and `LoadPsoEpisode3Async`; the linked pages distinguish implemented structures from unsupported game-specific variants.
