# Full-size GameCube ISO relocation — SDK workstream

This is the SDK implementation companion to [DolphinDotNet.Samples PR #21](https://github.com/capz/DolphinDotNet.Samples/pull/21), containing the complete [feature plan](https://github.com/capz/DolphinDotNet.Samples/blob/docs/iso-defrag-evacuation-plan-2026-11/StorageIntegrity/ISO_DEFRAGMENTATION_FEATURE_PLAN.md).

## Current implementation in this PR

**P1a: read-only allocation-scan foundation only.** `include/dnd_defrag_planner.h` and `source/defrag_allocation.c` provide:

- FAT32 cluster-free/run inventory using allocation entries, including selected active FAT.
- exFAT cluster-free/run inventory from the **allocation bitmap** located through the root-directory file entry (not inferred from FAT entries).
- Sector-reader and partition-offset reuse, 32-byte aligned buffers, fixed RAM overhead, bounded loops and unsupported-media errors.
- Direct one-extent capacity preflight for GameCube ISO lengths through 2,147,483,647 bytes.
- Deterministic native host tests for fragmented free space, a full-size 1,459,978,240-byte requested allocation, alternate partition offsets, corrupted volume parameters and exFAT bitmap failures.

**No destructive actions or user-facing API are enabled by this patch.** A suitable largest-free-run result is a *snapshot*, not a reservation. The caller must revalidate before actual filesystem writes. Unsupported TexFAT multiple-bitmap media fails closed.

## Subsequent SDK implementation phases (not yet implemented)

1. **P1b:** enumerate exact free intervals, file-to-cluster ownership, reserved/immovable ranges and cross-device volume identity. Derive a deterministic best candidate region and minimal *whole-file* evacuation plan with peak scratch-space accounting.
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
