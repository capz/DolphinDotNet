# TextureStrategy — sparse pages vs cell atlases

A DolphinDotNet **GameCube rendering proof of concept** for the Hypercube engine decision.

Build using `dotnet build samples/TextureStrategy/TextureStrategy.csproj -c Release` (normal DolphinDotNet SDK, devkitPPC/libogc2 requirements). Run the generated DOL in Dolphin or on real GameCube hardware.

**Controls:** A toggles representation; B exits; Z toggles the existing DolphinDotNet diagnostics overlay. No custom GUI is introduced.

**Compared workloads:** a stationary 12×12 textured board, identical generated pixel values, regular managed GX texture assets. In page mode, 144 independently bound 32×32 RGBA8 pages draw 144 six-vertex quads. In atlas mode, nine 128×128 RGBA8 texture atlases each contain 4×4 cells and render a batched 96-vertex mesh. Atlas mode uses nine draws; page mode uses 144. The sample uses ordinary texture bindings rather than virtual pages: it deliberately isolates the **draw granularity penalty**, not the complete streaming system. It is *not* a claim that page-vs-atlas workloads are GPU equivalent: atlas mode reduces texture changes and geometry submissions.

Each mode uses the same 384×384 useful image and **576 triangles total**. Both allocate all their textures at startup; no disk access occurs in the frame loop. These resources together consume approximately 1.125 MiB native texture storage in RGBA8 plus mesh overhead.

After every 120 frames, the standard debug overlay prints the selected mode and a **deterministic logical** 240-frame moving-window LRU trace comparing 48 independently resident 4 KiB pages with three 64 KiB atlases (both 192 KiB budgets). The trace uses the same visible cells, counts misses and bytes hypothetically requested, and **does not DMA data, measure storage latency, or implement actual ARAM-backed residency**.

## Decision protocol

1. Collect per-frame CPU/GPU diagnostics in both modes on **real GameCube hardware** with identical display mode; optionally compare Dolphin.
2. Extend to more representative triangulated scenes and note draw calls, CPU/GPU time, texture bind pressure, working-set size.
3. Run native ARAM + storage profiling separately before concluding that fewer atlas misses mean better streaming. An atlas miss may transfer **16×** a page's bytes.
4. Build a hybrid candidate with coarser patches for distant surfaces. Compare at equal visual quality and draw overhead.
5. Do not commit Hypercube to either strategy until a moving-camera, fault-tolerant streaming test proves it meets the frame budget.

**Known limitations:** no CMPR, gutters, mip hierarchy, camera-controlled page LOD, ARAM transfer, asynchronous I/O, or actual streaming in this PoC. Static synthetic layout; no performance claims until run on hardware.

This sample builds on existing DolphinDotNet `GraphicsDevice`, `Renderer`, `Mesh`, `Material`, `Texture2D`, `Controller`, and `DebugOverlay` APIs; it does not change shared runtime code.
