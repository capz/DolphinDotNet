# Full-size GameCube ISO relocation — SDK workstream

This is the SDK implementation companion to [DolphinDotNet.Samples PR #21](https://github.com/capz/DolphinDotNet.Samples/pull/21), containing the complete [feature plan](https://github.com/capz/DolphinDotNet.Samples/blob/docs/iso-defrag-evacuation-plan-2026-11/StorageIntegrity/ISO_DEFRAGMENTATION_FEATURE_PLAN.md).

## Current implementation in this PR

**P1a/P1b: read-only native ownership and planning.** `include/dnd_defrag_planner.h`, `source/defrag_allocation.c` and `source/defrag_planner.c` provide:

- FAT32 cluster-free/run inventory using allocation entries, including selected active FAT.
- exFAT cluster-free/run inventory from the **allocation bitmap** located through the root-directory file entry (not inferred from FAT entries).
- Sector-reader and partition-offset reuse, 32-byte aligned buffers, fixed RAM overhead, bounded loops and unsupported-media errors.
- Direct one-extent capacity preflight for GameCube ISO lengths through 2,147,483,647 bytes.
- Deterministic native host tests for fragmented free space, a full-size 1,459,978,240-byte requested allocation, alternate partition offsets, corrupted volume parameters and exFAT bitmap failures.
- Exact free intervals and a disjoint ownership map: FAT32 chains and exFAT FAT/NoFatChain streams, UTF-16 components, parent indices and physical directory-entry identities. Directories, system/read-only/hidden files, GCI saves, reserved staging names, undecodable names and unknown allocations are pinned. A read-only callback pins open or application-owned files.
- Caller-owned, capacity-limited file/run arrays; no heap allocation, recursion, data-file reads or cluster bitmap. Capacity exhaustion returns `-ENOSPC` with `valid == 0`. Cross-links, loops, bitmap disagreements and corrupt exFAT entry checksums fail closed.
- Exact deterministic candidate search using extent boundaries. Whole-file costs include source evacuation and mixed blocker/source cases, scratch allocation rounding on the scratch filesystem, explicit journal/directory reserve, transfer amplification and stable relocation order. The score is evacuation bytes, file count, transfer bytes, then cluster index.
- Native direct/ISO/blocker estimates, explicit mounted-ISO/live-source guards, caller-provided physical device identities and remount generations, and comparison against a fresh metadata scan (`-ESTALE`). A partition serial or mount name alone is **not** a physical device identity.
- Independent exhaustive subset/window oracle comparisons for 2,000 small volumes, and a virtual 1,459,978,240-byte FAT32 ISO with fewer than 1,500 metadata-sector reads and constant scanner workspace.

**No destructive actions or user-facing API are enabled by this patch.** A suitable largest-free-run result is a *snapshot*, not a reservation. The caller must revalidate before actual filesystem writes. Unsupported TexFAT multiple-bitmap media fails closed.

The ownership arrays are explicitly bounded by caller capacity, not by file size.
The scanner itself uses small fixed metadata buffers. Very fragmented/large
directory inventories can exceed the chosen map capacity; do not silently omit
files or mark an incomplete map valid. Directory traversal is iterative through
the file table. Unknown allocations remain unavailable to the planner.

The native planner is an estimator, not an execution authorization. The mounted
backend must supply trustworthy physical identities, write-firmware capability,
open-handle pins and DVD guards under a per-device lock. It must retain the
original snapshot and compare a fresh scan before each destructive transition.
Fingerprints detect accidental metadata changes, not malicious forgery. Callers
must exclude concurrent filesystem mutation while scanning; no software snapshot
can make independently changing raw metadata atomic. There is no managed
`Defrag.Plan/Execute` bridge yet. Full rebuild remains unsupported until P5.

## Subsequent SDK implementation phases (not yet implemented)

1. **P1b delivered (native read-only):** ownership, free intervals, pinned/unknown ranges, exact whole-file planner and snapshot identity contracts. Mounted-backend/public managed integration remains gated on execution contracts.
2. **P2:** upgrade full-size ISO stream/verified staging to report trustworthy progress, cancellation, 32-bit size safety, read/write amplification and resumable checkpoints. Avoid needless full-media reads without compromising verification.
3. **P3:** two-volume journaled ISO offload to independent SD Gecko A/B, SD2SP2 or writable GC Loader media, strict scratch verification and source release only after durable authority transfer, reinsertion/recovery idempotence and handle/DVD guards.
4. **P4:** offload only obstruction files necessary to clear the chosen ISO cluster region; restore them in verified contiguous allocations where possible, report partial-contiguity accurately, and recover after partial restoration.
5. **P5:** full external backup/rebuild mode and headless acceptance consumable through `DolphinDotNet.Samples` **after** the API is distributed by NuGet.
6. **P6:** optional separate experimental raw cluster-move engine; require independent metadata review, exhaustive fault injection and real-device acceptance. It is **not** an implicit fallback for normal defragmentation.

## Non-negotiable constraints

- Never delete the last verified copy and never trust arbitrary journal paths. Transaction records must exist on **both participating writable volumes** before cross-device destructive transitions.
- Reject same physical target/scratch card, unsupported write firmware, live file handles and any ISO currently backing mounted `dvd:/`.
- No claim that aligned sector writes alone imply physical contiguity. Confirm actual allocation extents.
- Keep managed/native staging memory bounded and preserve original filesystem metadata where representable.
- Planning/measurement do not mutate the volume. Physical power-cut tests, particularly on GC Loader firmware, remain required.
- Every SDK phase lands as a separately reviewed commit with native, compiler/AOT and GameCube regression coverage; do not update Samples to call unreleased methods.

## Cloud Codex starting task

Work against **`capz/DolphinDotNet`**, on **`feature/iso-defrag-planning-and-evacuation`**, keeping this PR open. Review the Samples plan and current native storage, GC Loader and journal infrastructure. **Continue P1b**: safe file ownership/extent enumeration and a deterministic read-only candidate planner with headless fixtures. Do not implement cross-device deletion until a tested two-volume journal exists. Commit each phase, push to this PR, and inspect CI; do not merge without successful checks. Report what requires actual hardware.
