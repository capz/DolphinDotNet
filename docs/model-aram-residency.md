# Bounded model residency

`Model.UnloadToAramAsync` and `Model.PrepareAsync` preserve shared model and
instance identities while moving immutable package bytes between MEM1 and ARAM.
The snapshot owns a 32-byte-rounded ARAM allocation. A model's package length is
available as `PackageBytes`; `AramBytes` reports the rounded backing length.
`SupportsAramResidency` is false for explicitly nonpageable/compacted models.

## Frame-loop integration

Set the policy before beginning a transition, then grant work between frames:

```csharp
model.AutomaticResidencyProgress = false;
model.ResidencyBytesPerStep = 16384; // 32..65536, multiple of 32
model.ResidencyWorkItemsPerStep = 64; // 1..1024
Task transition = model.UnloadToAramAsync(cancellationToken);

// During later update turns, after previous GPU readers have completed:
if (model.IsTransitioning)
    model.AdvanceResidency();
// Observe transition completion/failure before deciding to restore or draw.
```

`AdvanceResidency()` grants one bounded native step. It returns true when CPU
work, a state change or request submission progressed, and false while waiting
for DMA or broker capacity. An application may grant more steps while its
measured per-frame CPU allowance remains, stopping on false. The byte/work
settings are captured at transition start. They bound explicit hashing,
comparison, copying, descriptor walks, palette joints and output vertices;
individual native allocation/free calls remain indivisible and must be measured
under realistic memory pressure. These controls are work bounds, not a claim
that every step takes a particular number of microseconds on hardware.

Automatic progress remains the default. It uses one reusable 16 ms callback
timer, granting at most 16 progressed steps while a measured 1 ms CPU allowance
remains and stopping immediately on a DMA wait. This is a soft allowance: a
single primitive cannot be preempted. Low-level manual host tests
retain their existing explicit-scheduler behavior. Manual residency must keep
receiving grants until its task settles, including cancellation and disposal.
`IsTransitioning` and `AdvanceResidency` remain usable to drain a model disposed
while a manually driven transition is pending.

The model remains busy for the entire operation. All users of the shared model
must be inactive or already prepared for the transition; one invisible instance
is insufficient. Drawing and instance updates reject a transitioning or cold
model. The application must prefetch before revealing its users and must not
block its render loop on a pending task.

## Transfer and publication phases

Package DMA uses a retained native memory lease and the shared priority broker:
restoration is demand priority and offload is background priority. No managed
4 KiB bounce array or per-chunk Task is created. The owner remains alive until
the broker reports terminal completion, even when cancellation arrives during
DMA. Rounded package tails stay inside the owned aligned allocation.

The first snapshot computes SHA-256 incrementally from the already validated
immutable source as chunks complete. Restoration verifies that same digest as
chunks arrive. Only then does it rebase retained mesh/index/skin/animation
descriptors. This checks byte identity against the original validated package
without rerunning the full synchronous parser. New files still pass the existing
complete admission validation before becoming eligible for snapshots.

Texture-cache searches and collision comparisons are sliced. New shared texture
storage is reserved privately, copied/flushed in bounded spans, and published
only when complete. Temporary cache references protect existing candidate
images while a comparison spans multiple turns. Skinned outputs and joint
palettes rebuild in bounded steps from retained global matrices; animation
playback time and crossfade/override state do not advance during restoration.
The model becomes resident only after every dependent texture and live instance
is ready. Native package allocations are not synchronously zeroed before DMA;
the transfer fills all payload/padding bytes before publication.

Offload retirement converts package pointers into offsets and retires instance
skin buffers and last-owned texture references incrementally after GPU readers
have completed. Only then is the package released. Later demotions reuse the
immutable backing and run the same bounded retirement steps without another
snapshot upload.

## Cancellation, budgets and backing ownership

Cancellation before retirement begins retains the original resident model.
After retirement commits, late cancellation is ignored and demotion finishes
successfully cold. The caller can then restore if demand changed. Restoration
cancellation, integrity failure or allocation failure drains submitted DMA,
releases partial textures/skin outputs, returns descriptors to offsets and leaves
the model cold with its previous backing intact. Disposal during a transition
drains the same ownership before releasing the model.

Staged packages, transition context and provisional texture ownership count
against native model budgets. Instance palette/output allocation retains each
instance's budget. Source/backing/destination overlap must also fit the
application's global MEM1 and ARAM budgets. `Model.NativeBytes` charges shared
textures to each model; only last-owner release frees the physical image. Count
unique images and instance bytes separately when estimating actual MEM1 reclaim.

Backings remain in ARAM while the model is resident to accelerate repeat
demotion. `ReleaseAramBacking()` releases that redundant copy only for a resident,
nontransitioning model; it returns false if no backing exists. It rejects cold
or busy models so their only recoverable package cannot be discarded. The next
demotion will create a fresh snapshot. This permits an application-level shared
cold budget and eviction policy without silently changing model ownership.

Compact native adapters must set `DndModel.residency_disabled` when discarding
their full package. The native entry point checks that capability and the actual
package allocation size before taking a raw transfer lease; retained geometry
cannot be read using the former full-package length.
