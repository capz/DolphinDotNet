# Memory reduction audit

Baseline: SDK 318, revision `4c8cd26`. Managed allocation measurements below use
the host .NET 8 CLR. They are distinct from native section sizes and from the
GameCube managed heap. The changes retain the existing public APIs.

## Transform hierarchy caches

Each transform previously allocated its TRS values (`float[10]`), local matrix
(`float[16]`), world matrix (`float[16]`), a child `List<Transform>`, and that
list's empty backing array. Most batch transforms and root game objects never
use hierarchy storage.

The world matrix and TRS values remain eager, and each transform retains its
own stable world array. A never-parented transform now builds TRS directly into
that world array. Its separate local matrix is reserved when it is first
parented, before changing the hierarchy. A child list with initial capacity
four is reserved only on the first attachment to a parent. Caches remain
available after detachment so repeated hierarchy updates do not allocate.

Validation of cycles and complete subtree depth still precedes allocation and
link changes. The new parent's insertion finishes before removal from the old
parent, preserving the original graph if allocating those buffers fails. A root without a
local cache keeps its local matrix dirty even after computing world state, so
its first attachment recomputes current local TRS. Repeated changes and matrix
reads allocate no additional buffers.

| Warmed CLR allocation workload | Before | After | Reduction |
| --- | ---: | ---: | ---: |
| 128 root transforms, including their owning reference array | 47,128 B | 28,696 B | 18,432 B |
| 128 children attached to one existing root, including owning array and parent list growth | 49,288 B | 42,152 B | 7,136 B |

Each never-parented transform eliminates three allocations and 64 B of matrix
element payload. The host CLR reduction is 144 B per transform, including
object/array overhead. For Hypercube's 128 batch poses, this removes 8,192 B of
matrix payload and 384 allocation objects; the measured CLR constructor
reduction is 18,432 B. Parented leaves retain both matrix arrays but no longer
allocate their own empty child list and empty backing array.

The PPC32 layout gives a different overhead reduction. From `dnd_managed.h`,
the managed object header is 8 B and the array header is 24 B. The allocator
rounds objects to eight-byte boundaries and prefixes each with a 16 B heap
block. The generated `List<Transform>` instance has 16 B of fields after the
object header. Removing one 64 B matrix array (104 B including headers), one
empty list object (40 B), and its zero-length backing array (40 B) therefore
avoids **184 B per never-parented transform**, or **23,552 B for 128**. This is
a layout calculation, not a hardware heap measurement. It assumes fresh
allocations; already fragmented free blocks can consume different amounts.

Reproduce the host measurements from the repository root:

```sh
python3 scripts/measure_transform_allocations.py --revision 4c8cd26
python3 scripts/measure_transform_allocations.py
dotnet run --project tests/TransformMemoryTests -c Release
```

The probe exports the selected source revision into a temporary project and
does not modify the checkout. Focused CLR tests cover first attachment,
detachment/edit/reattachment, propagation to descendants, rejected cycles and
depth overflow, stable distinct world caches, and allocation-free repeated
updates. `ModelSmoke/TransformChecks.cs` exercises the same critical hierarchy
transitions through AOT with production native math. The graphics CI domain
runs the CLR checks and the model acceptance script runs native/AOT checks,
including managed GC stress.

## Further candidates and tradeoffs

* **Material sampling options:** options are already lazy, but assigning an
  explicit default sampler or UV transform still allocates `float[40]` (160 B
  element payload). Hypercube intentionally preallocates these arrays before
  batching to avoid frame allocations. Skipping default assignments alone
  would move a future nondefault allocation into material registration or
  updates. A shared default representation needs careful reset semantics
  because the renderer's native sampler state is global.
* **Managed model overrides:** every `ModelInstance` owns 10-float node and
  four-float color scratch arrays (56 B payload) even if no override is used.
  Making them lazy saves unused storage but adds a first-override allocation.
  The native color ABI requires exactly four floats, so sharing the 10-float
  buffer requires an ABI change. Native override storage is already lazy.
* **Crossfade poses:** `DndInstance.other` reserves `40 * nodeCount` B even
  when no crossfade is played. Laziness would defer allocation/budget failure
  from instance creation to the first fade. It needs explicit rollback tests
  for failed fade admission and animation state before implementation.
* **Render queue storage:** each queue entry is a separately allocated object;
  default capacity is 512. Inline struct entries could remove those objects
  and the reference array indirection, but all mutating paths and AOT reference
  scanning must be verified. Changing the two bounded sort index arrays to
  `ushort` would save `4 * capacity` B (2,048 B at default capacity), with
  compiler signedness and maximum-capacity coverage required.
* **Bulk draw matrices:** replacing transforms with one shared mutable world
  array is incompatible with `DrawMeshes` and `RenderQueue`, which borrow
  multiple distinct matrices in one submission. A flat matrix submission API
  could reduce object overhead while preserving that lifetime contract, but
  requires separate API and renderer validation.
* **Streaming textures and native reads:** both texture images protect active
  GPU readers during staging/publication. `Stage` retains its native source
  until completion, and publication checks the destination fence. AssetStream
  native reads already write directly into the destination; their 4 KiB bounce
  buffers are temporary stack storage. Removing an image or the conservative
  `NativeBuffer` release wait needs a new ownership contract, including callers
  that expose raw addresses to GX.
* **Native alignment and shared scratch:** resource allocations may reserve up
  to `alignment-1` extra bytes (usually 31 B, up to 4,095 B); a portable aligned
  allocator and matching free/accounting path need validation before changing
  this. Model overrides and ordinary mesh drawing also reserve separate
  768-vertex workspaces, a possible 27,648 B saving. Sharing them requires an
  explicit executor/reentrancy contract; the larger immediate/glyph workspace
  has different capacity requirements.

Native persistent and scratch reductions are described below. Their presence
in an executable depends on the native linker retaining the relevant code;
they must not be added unconditionally to the managed savings above.

## Native model batch submission scratch

The existing 4,096-mesh batch limit applies to the total number of opaque and
alpha meshes. Previously, each pass reserved its own 4,096-entry table even
though both could not be full at the same time. A single table now grows opaque
entries from its front and alpha entries from its back. Opaque and alpha slices
are still sorted independently. Original sequence numbers preserve alpha order
for equal depths despite insertion from the back.

`ModelSubmission` is 16 B, so replacing two tables with one saves **65,536 B of
static native storage** when `dnd_model_draw_models` is retained by the linker.
Production `model_graphics.o`, built with the recording fixture flags, changed
from 158,904 to 93,368 B of BSS. Its text changed from 16,296 to 16,307 B (+11 B)
on the host compiler. Those object-file figures are compiler-specific, and
linker section collection can omit this scratch entirely from an application
which only draws raw meshes.

Recording tests verify every submitted mesh for 4,096 all-opaque, all-alpha and
mixed batches in both cached and uncached state modes. They also check stable
equal-depth order and rejection of a 4,097-mesh batch before any draw occurs.
No heap allocation was added to submissions.

## Native model skin workspace

Skin evaluation previously reserved space for 256 position matrices and 256
normal matrices for each skinned model instance: `256 * 32 * sizeof(float)`,
or 32,768 B of payload. Model skins are immutable after loading and each has a
validated joint count from one to 256. Each new workspace allocation caches the
maximum joint count across the model's skins and reserves both palettes using
that count. Their separate matrix strides remain intact.

| Maximum skin joint count | Previous workspace charge | New workspace charge | Workspace reduction |
| --- | ---: | ---: | ---: |
| 1 | 32,800 B | 160 B | 32,640 B |
| 2 | 32,800 B | 288 B | 32,512 B |
| 256 | 32,800 B | 32,800 B | 0 B |

The native allocator charges a 32 B allocation header plus a rounded payload;
these sizes include that charge. The cached maximum adds four bytes to the
PPC instance structure (eight bytes after host pointer alignment), so a model
with two-joint skins saves **32,508 B net per instance on PPC**, or 32,504 B on
the host. A maximum-size skin retains its previous workspace size and adds
only the cached field. Instances without evaluated skins do not allocate a
workspace.

The maximum is computed only when allocating workspace, avoiding a scan on
ordinary updates.
Native byte accounting, budgets and allocator alignment remain in use. The
workspace retains instance ownership and is freed by vertex dropping, model
unload and instance disposal. Dropping the workspace also clears its cached
stride; recreation derives a fresh bound from the resident model's validated
skins.

`tests/model_skin_workspace_test.c` uses production allocation and animation
code. It verifies one-, two- and 256-joint skins, mixed one-/256-joint skins,
position and normal transforms under nonuniform scale, reuse without new
allocation, drop/recreation with changed valid skin counts, exact-budget
admission, and one-byte-under-budget failure cleanup. Run the complete native and AOT model acceptance with generated
fixtures:

```sh
dotnet run --project tests/ModelAssetTests -c Release -- /tmp/dnd-model-fixtures
python3 scripts/ci_model_tests.py /tmp/dnd-model-fixtures
```
