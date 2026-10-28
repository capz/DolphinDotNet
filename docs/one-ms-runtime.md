# Rendering and AOT throughput

This work targets foreground execution time on GameCube, without adding worker threads. The engine evidence is in the accompanying Hypercube worktree under `docs/measurements/one-ms`.

The changes use GX indexed arrays for uniform-color unlit meshes, keeping the existing vertex allocation and index ordering. Lit, generated-UV and varying-color meshes retain their previous path. Mesh creation/update invalidates the vertex cache. Switching to immediate geometry restores vertex/channel state.

`OverlayGlyphMode.Atlas` adds one nearest-filtered I4 quad per character. Existing geometry modes remain available. Stable console geometry is cached as a geometry-only GX command list; state is configured outside the list. A completed-frame serial prevents rewriting in-flight commands, allocation failure falls back to immediate text, and shutdown releases caches after a GPU wait. Two caches are bounded to approximately 146 KiB combined; allocation is lazy.

GameCube release bundles default to `-O3 -flto -fno-strict-aliasing`; symbol builds and other platforms retain their previous defaults. `DND_LTO=0` retains a comparison control. A content-sensitive native-flags dependency rebuilds objects and relinks when flags change.

The AOT compiler emits checked typed array access and fast poll guards. Small, acyclic methods composed only of proven non-allocating primitive operations or bounded known callees can omit polls and their GC frame. Unknown calls, virtual dispatch, loops, exception regions and static initialization remain conservative. Debug source-location builds retain their original rooting and polling behavior. Hot helpers inline in optimized builds; exception machinery stays out of line. CRC32 uses a constant 1 KiB table with the existing seed/range contract.

Validation includes native rendering/cache/CRC tests, primitive widths and error precedence, compiler integration, native flag switching, and Debug/Release reference, timer and leaf fixtures under GC stress. Dolphin measurements retain scene, movement, streaming budget and audio checks. Hardware timing is not yet validated. Legacy profiling controls remain flagged for removal after performance validation.

The best measured engine run is approximately 1.84 ms foreground mean with improved detail readiness, versus 12.29 ms on the previous texture branch. This excludes GPU/VI blocking, explicit yield, and background work overlapping those waits. Display intervals remain approximately 16.7 ms. The 1 ms target has not been reached.
