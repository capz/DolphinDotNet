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

## SDK phase status and remaining integration

1. **P1b delivered (native read-only):** ownership, free intervals, pinned/unknown ranges, exact whole-file planner and snapshot identity contracts. Mounted-backend/public managed integration remains gated on execution contracts.
2. **P2 native streaming delivered:** `dnd_verified_copy` uses one caller-owned 32 KiB aligned buffer, SHA-256, synced durable-prefix checkpoints, resumed prefix verification against both copies, final reopened stage verification, cancellation and 64-bit measured counters. `dnd_optimize_file_ex` integrates the engine into direct relocation with physical extent checks, metadata preservation and SHA-256 version-two journals; it can still recover version-one journals. The extra immediate stage digest pass was removed. Direct PREPARE recovery deliberately rolls back partial stages; persistent resume of that path and a managed progress bridge remain future integration work. Four file-length reads and one write are measured for a successful direct replacement (stream, stage verification, source revalidation and published-copy recovery verification). The shared streaming primitive itself needs two reads per written byte for a fresh copy. Host tests actually generate and verify all 1,459,978,240 virtual bytes without an ISO-sized allocation. Throughput on physical media remains unmeasured.
3. **P3 native protocol delivered, platform binding disabled:** versioned, canonical little-endian 1,536-byte SHA-256 journal records in alternating slots on both volumes; synced readback, immutable operation/device/path metadata, adjacent-checkpoint reconciliation, explicit missing-media states, and no advance with mismatched replicas. `dnd_evac_run` composes resumable P2 streaming with verify-before-release, contiguous restore, create-new publication and verify-before-scratch-cleanup. Every invocation binds recovery to an independently trusted request, not a journal-provided path. The injected filesystem contract requires two-volume locks, identity/partition checks, writable firmware, handle/DVD guards, stage collision checks and metadata preservation. Tests cover torn journal bytes, every protocol state and 288 interruptions after journal/filesystem/I/O callbacks followed by reboot and repeated resume. These are host models, not physical power-cut results. No GameCube implementation of these callbacks or managed public Execute/Resume/Rollback API is enabled. Rollback of an evacuated file currently means safely completing restoration through resume; no generalized destructive rollback exists.
4. **P4 native coordinator delivered, platform binding disabled:** `dnd_defrag_batch_run` persists and rereads an immutable, SHA-256-bound complete manifest on both devices before any offload. It rejects duplicate paths/operation IDs, unbound hashes, mismatched device metadata and manifest collisions. Per-file P3 records provide independently recoverable authority while the coordinator evacuates the ISO when needed, evacuates blockers, rechecks the free window, relocates/verifies the one-extent ISO, and restores blockers. Blockers may be restored fragmented only with explicit physical extent reporting; ISO verification always requires one extent. Resume rechecks the same manifest and per-file journals. Tests cover 37 coordinator/manifest interruption points, mixed ISO/blocker and source-retained strategies, capacity failure after offload, conflicting manifests, repeated recovery and fragmented-blocker reporting. No mounted backend or general execution API is enabled.
5. **P5 read-only backup/rebuild preflight delivered; execution deferred:** `dnd_defrag_rebuild_plan` inventories every regular file (including protected files and GCI saves), directory count, per-file scratch/restore rounding, peak capacity with explicit manifest reserve, measured-copy I/O estimates and ISO-first/largest-allocation-first restore ordering. Unknown allocations, active internal transactions, unsupported names, stale/corrupt geometry, oversized files, mounted ISO/live handles and missing/same-device/insufficient scratch fail preflight. `can_execute` is explicitly zero and the reason is `NeedsOfflineAcceptance` even when backup capacity is available. No formatter, whole-card deletion, directory reconstruction or hidden fallback is exposed. A complete verified metadata manifest and a separately authorized offline rebuild backend still need implementation and physical acceptance. Do not add a headless Samples consumer until the managed API is implemented and distributed by NuGet.
6. **P6 offline-image engine delivered in dependent draft [PR #160](https://github.com/capz/DolphinDotNet/pull/160); physical execution disabled:** the [experimental raw-mover design and tests](iso-defrag-p6-experimental.md) now has a FAT32/exFAT move-window engine, replicated sector-image journal, verified undo/redo, bounded streaming and a separate offline image tool. Dedicated CI covers sector faults, independent mkfs/fsck images, full-size virtual ISOs, sanitizers and PowerPC32 big-endian execution. Independent metadata/protocol review, GameCube exclusive raw-mode/pre-mount integration and physical power-cut acceptance remain required. `DND_RAW_DEVICE` returns unsupported, and normal `Defrag.Execute`/automatic defrag cannot select P6. P1–P5 green CI does not establish raw-mover safety.

## Non-negotiable constraints

- Never delete the last verified copy and never trust arbitrary journal paths. Transaction records must exist on **both participating writable volumes** before cross-device destructive transitions.
- Reject same physical target/scratch card, unsupported write firmware, live file handles and any ISO currently backing mounted `dvd:/`.
- No claim that aligned sector writes alone imply physical contiguity. Confirm actual allocation extents.
- Keep managed/native staging memory bounded and preserve original filesystem metadata where representable.
- Planning/measurement do not mutate the volume. Physical power-cut tests, particularly on GC Loader firmware, remain required.
- Every SDK phase lands as a separately reviewed commit with native, compiler/AOT and GameCube regression coverage; do not update Samples to call unreleased methods.

## Cloud Codex starting task

Work against **`capz/DolphinDotNet`**, on **`feature/iso-defrag-planning-and-evacuation`**, keeping this PR open. Review the Samples plan and current native storage, GC Loader and journal infrastructure. **Continue P1b**: safe file ownership/extent enumeration and a deterministic read-only candidate planner with headless fixtures. Do not implement cross-device deletion until a tested two-volume journal exists. Commit each phase, push to this PR, and inspect CI; do not merge without successful checks. Report what requires actual hardware.
