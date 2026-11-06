# Indexed vertex-color submission

Unlit meshes with varying vertex colors and identity material tint now use GX
indexed position/color/UV arrays. Previously only uniform-color meshes used this
path; colored glass expanded indices into 768-vertex CPU staging blocks on every
draw. The existing native vertex allocation supplies all attributes directly.
There is no additional persistent allocation or authored-geometry change.

Uniform-color draws retain register color/tint. Varying-color draws use indexed
RGBA8 from `DndDrawVertex.color`, with the same vertex index for position, color
and each enabled UV stream. Layout/source changes invalidate GX's vertex cache.
Switching back to direct or uniform drawing restores descriptors/channels through
the existing transitions. Dynamic mesh updates already invalidate the cache.

Nonwhite material tint keeps the direct path so its integer per-channel multiply
and divide-by-255 behavior is preserved. Lit/generated-UV paths and disabled
`DND_INDEXED_MESHES` controls also retain their fallbacks. This is deliberately
not hardware instancing, geometry merging or approximate tinting.

Controlled VideoMode experiments apply this prototype to a copied native bundle
from published SDK .217, keeping its compiler and other runtime sources fixed.
The backport adapts draw-count instrumentation because .217 counts within
`draw_state`, while current main counts at GX submission. These are prototype
measurements, not measurements of a newly published SDK package:

| Fixed Options, Z visible | App submit | Work excluding VSync | Frame |
| --- | ---: | ---: | ---: |
| Published .217 baseline | 11.746 ms | 20.354 ms | 33.364 ms |
| Color-index prototype | 9.993 ms | 18.625 ms | 33.364 ms |
| Prototype plus cached sample footer | 7.851 ms | 16.468 ms | 16.681 ms |

The final row barely crosses a VSync deadline in this fixed scene; it does not
establish 60 fps during animation or on physical hardware. Mesa/Dolphin guest-clock
observations are not GameCube measurements. The prototype also has a roughly
0.35 ms blur-stage regression in this backport; the table reports net work rather
than just the favorable glass scopes. Native images have zero changed pixels
outside timing values. Raw CSVs, captures, hashes and the controlled-backport
script are retained in the companion Samples submission report.

Validation: GX descriptor/index tests cover color arrays, index order, uniform
transitions, tinted/lit fallbacks, cache changes, UV streams, draw counters and
triangle splitting. Graphics phase-one/two AOT/GC host fixtures pass. The Hypercube
rendering integration fixture is compiled against this source SDK, separately
from the .217 prototype timing control.
