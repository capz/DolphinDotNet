# Camera API allocation and culling costs

Rendering-only cameras allocate inverse matrices only on first Unproject or
CreatePickingRay. This avoids 192 bytes of float-array payload and two array
headers until picking is used. Six cached plane lengths cost 24 bytes and one
array header, yielding a net saving of 168 payload bytes and one array header
for a rendering-only camera. Picking cameras retain the extra 24-byte cache.

Sphere culling computes six square roots on the first sphere after a combined
view/projection matrix change, then reuses those lengths for subsequent spheres.
A box-only workload does not compute plane lengths. Matrix invalidation includes
LookAt, projection changes and display-aspect changes through PrepareDisplay.
Plane equations and comparison arithmetic are unchanged; planes are not normalized.
The first sphere can perform more square roots than the old early-rejection path;
the benefit is repeated culling against the same camera.

GraphicsPhaseOneSmoke compares 61,200 sphere queries against an analytic oracle
across perspective/orthographic and translated cameras, with repeated queries.
Existing project/unproject and picking tests run through Debug/Release AOT and
the native runtime with GC stress. These checks establish correctness, not a
physical GameCube frame-rate improvement.

The API review also confirmed existing native staging buffers, range-based mesh
creation and in-place texture replacement support reusable caller storage.
BitmapFont.LoadAsync still copies its embedded texture package: eliminating that
copy would require a separately validated package-range native API.
