# P6 — experimental in-place cluster relocation

Companion to the [SDK workstream](large-iso-defragmentation.md).

**Status: experimental offline-image engine implemented in dependent draft
[PR #160](https://github.com/capz/DolphinDotNet/pull/160); physical execution
disabled.** The native engine, replicated sector-image journal, verified undo/redo
and a separate image-only tool are implemented. Independent metadata/protocol
review, GameCube exclusive raw-mode/pre-mount integration and physical acceptance
remain release gates. `DND_RAW_DEVICE` returns `ENOTSUP`; no bypass switch exists.
PR #159's earlier green CI results do not validate these new P6 changes.

The [image tool instructions](../tools/raw-move/README.md) describe runnable
planning, execution, inspection and recovery. This is a bounded move-window
engine: a successful partial move reports its actual extent count, and does not
claim that the whole ISO has become contiguous.

P6 would relocate file data between physical clusters on the **same volume** and
update the filesystem's allocation metadata. This can avoid staging a second
complete ISO on the target card. It adds risks that filesystem-managed copying
avoids: a torn FAT entry, bitmap update or directory entry can make an otherwise
intact file unreachable, cross-link unrelated files or expose a partially updated
allocation map to a mount.

P6 must remain unavailable through normal `Defrag.Execute`, automatic defrag and
recovery of P2–P5 transactions. An unavailable strategy returns an explicit
unsupported result. There is no automatic raw-write fallback.

## Scope of the first experiment

Start with disposable filesystem images and one regular file whose logical
length does not change. Move only to clusters proven free in a fresh ownership
scan. Exclude directories, filesystem metadata, unknown ownership, protected
files, GCI saves, open files and an ISO backing `dvd:/`. A transfer buffer stays
bounded even when a cluster is larger than that buffer.

The first implementation should support FAT32 before attempting exFAT writes.
Start with a single supported FAT policy, 512-byte sectors and an explicitly
supported partition layout. Reject unimplemented mirror policies, sector sizes,
TexFAT, unsupported entry extensions and ambiguous metadata. A read-only scan
recognizing a filesystem is not evidence that its raw-write variant is supported.

Do not implement occupied-cluster swaps or relocation cycles in the first
version. A move graph must terminate at verified free destinations, with enough
reserved space for its current transaction window and journals. Cycles need a
separately designed temporary-cluster protocol; otherwise return unsupported.

An in-place operation does not imply recovery without another device. The first
physical experiments require an independently verified full backup on another
physical device, as well as recovery-journal capacity. Removing that backup
requirement would need a further explicit review and acceptance gate. The backup
is a recovery aid, not permission to weaken the mover's own safety invariants.

## Entry conditions and the raw-write boundary

Before generating a writable plan, the platform must establish all of the
following:

- Stable physical media identity, partition identity, geometry and media
  generation. A mount name or filesystem serial alone is insufficient.
- Verified write and flush capabilities for the actual driver and firmware.
  Unknown GC Loader capabilities remain unsupported.
- Exclusive control of the physical volume: no live file/directory handles,
  mounted ISO consumers, background filesystem writers or competing mounts.
- A complete ownership snapshot and an authoritative file manifest containing
  logical length, full-file SHA-256, metadata and bounded logical-chunk hashes.
- A bounded move window, its exact data destinations, metadata-sector write set,
  journal capacity and source/destination ownership proofs.
- A tested recovery-discovery path that runs before an ordinary filesystem mount.

Filesystem-managed setup creates and reserves journal storage before raw access.
Then the platform flushes and closes the filesystem, drains pending I/O,
invalidates filesystem and block caches, and enters exclusive raw mode. Holding
an application lock while a filesystem remains independently writable is not
sufficient. Returning to mounted mode requires recovery/verification first and
fresh caches afterward.

The raw writer receives a validated operation-specific allowlist. It cannot
accept arbitrary journal LBAs or arbitrary file paths as write authorization.
Every patch must identify its partition-relative sector and the exact fields it
owns. Bytes belonging to neighboring files in the same sector remain unchanged.
Boot/partition tables, directory allocation chains, bitmap/upcase files and other
system allocations are never relocation targets. Any required dirty-flag or
filesystem-hint update needs its own reviewed handler; it is not a general
exception permitting boot-region writes.

## Metadata that must be handled

| Filesystem | Required changes and checks |
|---|---|
| FAT32 | Reserve the destination allocation; connect its FAT entry to the correct successor; redirect the predecessor, or update both first-cluster fields in the file's directory entry when moving the head; release the old allocation only after commit. Preserve the reserved upper FAT-entry bits. Honor the selected active FAT and the exact reviewed mirroring policy. |
| FAT32 hints/state | Specify dirty-state handling and the validity of FSInfo free-count/next-free hints, including supported backup copies. A net-zero cluster move does not justify guessing that every intermediate hint or dirty-state update is harmless. |
| exFAT FAT-chained file | Update the allocation bitmap as the allocation authority, FAT links and stream first-cluster fields where necessary. Preserve logical length, valid-data length, attributes and timestamps, and recompute the directory entry-set checksum. |
| exFAT `NoFatChain` file | A move that breaks its contiguous allocation cannot leave `NoFatChain` set. Construct and validate the complete replacement FAT chain before changing allocation mode. Mode conversion must itself be recoverable and bounded in resident memory. Return to `NoFatChain` only after verifying one physical run. |
| exFAT entry sets | Account for entry sets crossing sectors and directory clusters. Journal every affected sector, preserve all unrelated entries and reject unimplemented extensions. Multiple directory-sector writes are not atomic. |
| exFAT system state | Preserve bitmap/upcase ownership and specify dirty/usage fields and boot-region checksum rules from the pinned filesystem specification before implementing any such writes. TexFAT remains unsupported. |

Pin the Microsoft FAT32 and exFAT specification revisions used for review. Use
independently generated images to check field interpretations; the SDK parser
and SDK writer agreeing with one another is not an independent oracle.

## Recovery journal and pre-mount discovery

P6 needs a **new raw-move journal format**. P3's 1,536-byte evacuation record does
not contain sector before/after images and must never be reinterpreted as one.

The journal must bind the operation ID, sequence, format version, physical media
and partition identities, geometry, original ownership/file manifest, logical
move ranges and allocation reservations. Its patch inventory contains full
sector before/after images, sector addresses, checksums and the allowed changed
fields. Commit records bind the complete inventory, not just its last page.
Large inventories and chunk-hash manifests are streamed from journal storage;
they do not become a full-volume bitmap or an ISO-sized in-memory structure.

Use independently checksummed alternating checkpoints and replicated recovery
metadata on the participating writable volumes. A successful checkpoint requires
sync and readback. Keep the previous recoverable checkpoint until the next one
is durable. Preserve both journal copies until completion is verified. Account
for all journal storage and growth before reserving a move window.

Discovery is a release blocker: recovery cannot depend on successfully mounting
the filesystem whose metadata may be torn. The implementation must demonstrate
a bootstrap path whose locator and metadata dependencies cannot be damaged by
the allowed write set, or an independent recovery loader with validated physical
journal locations. Merely putting a journal in a root-directory file does not
solve this. Missing recovery media or an undiscoverable/ambiguous journal keeps
the target unmounted and read-only; it never triggers an ordinary mount or a
best-effort metadata repair.

Checksums detect corruption, not malicious authorization. Recovery validates the
write-set classification, identities, operation binding and patch bounds before
replaying anything. An unexpected sector or foreign modification outside the
recorded owned fields requires manual inspection. Deliberately torn writes to
an authorized pending sector are different: its durable intent and complete
before/after images must make deterministic undo or redo possible.

## Move-window protocol

All states below are proposed P6 states, not existing SDK enum values. Each
transition checks device identity and exclusive ownership again. A state is
usable for destructive progress only after the required replicas agree and have
been synced and reread.

| State | Work and permitted next action |
|---|---|
| `Prepared` | Persist the immutable file/move manifest, patch inventory, reservations and recovery locators. Verify source logical chunks. No source allocation is released. |
| `DestinationVerified` | Stream source data to the reserved free destinations; flush, reopen through the raw reader and compare affected logical-chunk hashes. Original source data remains authoritative. |
| `MetadataIntent` | Persist the complete metadata before/after images and the intended ordering before writing any owned metadata sector. |
| `MetadataApplied` | Apply destination allocation and mapping/entry-set changes, with checks and readback. Keep old source clusters reserved and intact. Verify the candidate logical mapping against the authoritative chunk manifest. |
| `Committed` | Durably commit the new mapping on the required journal replicas. Only now may recovery treat the destination mapping as authoritative. |
| `CleanupIntent` | Persist the cleanup patch inventory before releasing old source allocations or updating any reviewed hints. No old cluster may be reused during unresolved cleanup. |
| `WindowComplete` | Apply and verify cleanup; reconcile allocation counts and ownership. A subsequent window may start from a fresh validated map. |
| `OperationComplete` | Reconstruct and hash the entire logical file, verify metadata and physical extent count, check unrelated files/allocation invariants, and finalize journal/dirty-state handling before mounting. |

A bounded window must fit the declared patch budget. If several writes touch one
sector, coalesce them or record their exact dependency order; independently
undoing overlapping sector images is unsafe. Shrink an oversized window or
return a precise capacity error. Do not silently allocate another cluster-sized
buffer or discard rollback data.

Before the commit boundary, recovery restores the old mapping from validated
before images and retains the original source allocation. After the commit
boundary, recovery finishes the committed mapping and cleanup from validated
after images. This is not a promise of rollback after old clusters have been
released or reused. Every recovery action is idempotent and can itself be
interrupted and resumed.

Cancellation before commit uses the recoverable old mapping. Cancellation after
commit stops at a completed recovery window with the committed file intact.
It must not free a source allocation merely because a progress callback ran.

Logical-chunk hashes allow a bounded window to verify moved bytes and unaffected
parts of their logical chunks without rereading the whole ISO for every cluster.
The final full-file SHA-256 remains mandatory. Report the measured reads, writes,
metadata writes, verification passes and journal overhead separately; moving one
window is not equivalent to achieving one extent for the whole file.

## P6 implementation milestones

Current implementation coverage:

| Milestone | Delivered | Remaining gate |
|---|---|---|
| P6.0 | Native contracts, canonical records, operation-specific regenerated sector allowlist, bounded workspace | Independent specification/metadata/protocol review |
| P6.1 | FAT32 image moves, head/middle/tail updates, active/mirrored FAT handling, FSInfo preservation, undo/redo | Physical-device acceptance |
| P6.2 | exFAT image moves, allocation bitmap, cross-cluster entry sets, checksums, both NoFatChain conversions | Fragmented bitmaps intentionally unsupported; independent review |
| P6.3 | Exclusive offline image adapter, persistent identity/request anchors, explicit recovery-before-mount decision, cancellation, PowerPC32 big-endian tests | Actual GameCube driver, exclusive unmount/cache lifecycle and automatic pre-mount discovery |
| P6.4 | No physical-device writes enabled | Sacrificial-media interruption/removal and flush acceptance for every supported device |
| P6.5 | Separate image-only research interface | Public hardware opt-in API remains unavailable until all preceding gates pass |

The milestones below retain the release requirements; image tests alone cannot
authorize advancing a physical-device gate.

1. **P6.0 — specification and contracts:** threat model, supported variants,
   metadata field catalogue, mount/recovery bootstrap design, journal wire
   format, memory/storage budgets and API outcomes. Independent review first.
2. **P6.1 — disposable-image FAT32 engine:** free-destination moves, head/middle/
   tail updates, sector-image journal and interrupted recovery. No hardware raw
   writer or normal execution integration.
3. **P6.2 — disposable-image exFAT engine:** bitmap/FAT consistency, entry-set
   checksums and recoverable `NoFatChain` conversion, each as a separately tested
   subphase. Unsupported variants remain rejected.
4. **P6.3 — filesystem and runtime integration:** exclusive raw-mode adapter,
   cache lifecycle, pre-mount recovery discovery, bounded I/O, cancellation and
   32-bit PowerPC builds. Image-mode acceptance must pass before raw hardware.
5. **P6.4 — physical acceptance:** separately authorized sacrificial media,
   verified independent backups, power interruption and media-removal matrix
   for each driver/filesystem/firmware combination.
6. **P6.5 — explicit experimental API:** only after the preceding gates pass.
   Separate opt-in build/runtime controls, a read-only inspection/dry-run entry
   point, operation-specific authorization and explicit recovery outcomes.
   Normal `Defrag.Execute` still cannot select it.

## Tests required before enabling P6

These are release requirements. PR #160 adds the image-level tests described
below; unperformed hardware and independent-review requirements remain open.
The ownership/planner, copy, evacuation and batch tests are prerequisites; their
callback models do not replace sector-level raw-move tests.

### Reproducible image implementation checks

`scripts/ci_raw_move.py` runs the following separate suites, also scheduled in
`.github/workflows/raw-move.yml`. CI retains logs and a JSON report identifying
the exact commit, platform and installed tool versions. The existing native
suite runs quick cases under both normal and O3/LTO builds; storage CI runs the
quick raw-move cases separately.

- `faults`: all 513 prefix cuts for every raw sector write across mirrored and
  active FAT32, FAT-chained exFAT and NoFatChain exFAT (15,390 execution cuts),
  plus every mutation-callback boundary; repeat recovery, every prefix cut during
  the modeled undo/redo writes, persisted metadata-write subsets, zero-filled
  metadata recovery, sampled suffix/mixed writes, dropped writes, failed reads
  and syncs, cancellation at every modeled mutation boundary, and
  forged-but-rehashed inventory rejection.
- `independent`: images created by `mkfs.fat`/`mkfs.exfat`, checked using
  `fsck.fat -n`/`fsck.exfat -n`; independent logical-chain/hash verification,
  protected-file and backup checks, first/middle/tail moves, Unicode entry sets
  crossing sectors/clusters, both NoFatChain conversions, missing lookup caches,
  stale generation/request anchors, missing journals and interrupted execution.
  Partial and zero exFAT ValidDataLength cases preserve that field and hash the
  uninitialized logical suffix as zeros while verifying exact raw window bytes.
- `large`: a deterministic virtual 1,459,978,240-byte ISO moved and checked with
  32 KiB and 64 KiB clusters through a 32 KiB transfer buffer. All logical bytes
  are generated/read/written/hashed; neither an ISO-sized allocation nor a full
  volume bitmap is used. This virtual backup is reconstructible fixture data;
  the real host adapter separately hashes its entire actual backup image.
- `sanitize`: quick codec/move/recovery cases with address and undefined-behavior
  sanitizers. `powerpc`: static PowerPC32 big-endian Linux binaries under
  `qemu-ppc`, with compile-time pointer-size/endianness assertions. Linux emulation
  does not establish GameCube driver or hardware behavior.

Local independent-tool review used dosfstools **4.2** (release archive SHA-256
`64926eebf90092dca21b14259a5301b7b98e7b1943e8a201c7d726084809b527`)
and exfatprogs **1.2.8** (`d742b17af7b09370b9334deff167222f21720577c6eea3f9b3c51770ac544516`).
CI builds this checksum-pinned exfatprogs release using
`scripts/ci_raw_move_tools.py`: Ubuntu's older 1.2.2 checker rejects the untouched
cross-cluster entry-set fixture before the mover runs. Fixture/file SHA-256 values
are printed in the retained independent-test log.
exFAT field interpretation was checked against Microsoft's
[`exfat-specification.md`](https://github.com/MicrosoftDocs/win32/blob/3574c9389fce6d9a8bb0920511bd2551f9a21a4e/desktop-src/FileIO/exfat-specification.md)
(document date 2025-07-08). FAT review target is FAT32 File System Specification
1.03 (2000-12-06); independent review of that source and both handlers is still
required before physical support.

The exFAT specification allows a chained allocation bitmap, but exfatprogs 1.2.8
reads its bitmap contiguously in `fsck/fsck.c`. The raw engine therefore rejects
fragmented allocation bitmaps during read-only preflight. A read-only ownership
scanner accepting that variant does not enable writes. This conservative gate
avoids publishing images that a supported independent checker misinterprets.

The caller supplies fixed-capacity file/run arrays and one aligned 32 KiB transfer
buffer. The host tool caps those arrays at 256 files and 8,192 runs, failing with
`ENOSPC` rather than growing with the volume. Temporary coalesced metadata images
spill into an operation-private disk builder; each authoritative replica contains
a 512-byte request, two 512-byte checkpoint slots and 2,048 bytes per patched
sector, plus a dispensable LBA index. Both complete inventories are allocated,
synced and read back before the first image write, so journal-capacity failures
at preparation cannot publish a partial mapping. No journal evidence is deleted
automatically. An incomplete initial journal remains quarantined, with the
byte-identical original backup retained.

The Linux image adapter also persists an independent request-hash anchor and
random generation token as working-image extended attributes, syncs their file
and new journal parent directories, checks identities before every raw I/O, and
rechecks the complete backup digest when opening recovery. Preserving two journal
copies is insufficient if the trusted request/image identity is lost. These host
file identities and advisory locks are not physical media identity or a substitute
for the unimplemented GameCube exclusive raw-mode boundary.

### Independent filesystem fixtures

Use disposable FAT32/exFAT images produced by pinned independent filesystem
tools, alongside small synthetic images whose complete state can be enumerated.
Fixtures cover:

- Empty, nearly full and fragmented media; multiple partition offsets; FAT-sector
  boundaries; different cluster sizes, including clusters larger than 32 KiB.
- Moving first, middle and final clusters; one-cluster files; a partial final
  logical cluster; predecessor and successor entries sharing a FAT sector.
- FAT32 active-FAT selection, supported mirroring, reserved entry bits and FSInfo
  variants; unsupported variants must fail before the first raw write.
- exFAT bitmap boundaries and fragmented bitmap chains; FAT and `NoFatChain`
  files; directory entry sets spanning sectors/clusters; Unicode names; valid-
  data-length preservation; conversion into and out of contiguous allocation.
- Cross-links, loops, unknown ownership, reserved/bad clusters, stale ownership,
  malformed entry checksums, journal collisions and overlapping patch sectors.
- A deterministic 1,459,978,240-byte virtual ISO, signed-32-bit boundary inputs,
  overflowing LBA/length calculations and explicit rejection above `INT32_MAX`.

### Fault exploration

For every state and each data, journal, metadata, flush, readback and cleanup
operation, interrupt immediately before and after it. Restart with fresh caches
and execute recovery repeatedly, including interruption during recovery itself.

For a 512-byte sector write, exhaust all 513 prefix cut positions in the small
model; include suffix cuts, selected old/new byte mixtures, dropped writes,
zero-filled sectors, checksum corruption and delayed or failed flushes. Enumerate
write-reordering combinations within an explicitly bounded model. Larger images
use deterministic seeded fault campaigns whose seeds and traces are retained.
Do not describe sampled arbitrary mixtures or a bounded model as exhaustive
coverage of every possible card-controller failure.

Remove either medium at each phase, including between replica syncs. Reinsert the
correct medium, a stale partner, a different partition, a cloned filesystem
serial on a different physical device, or no partner. The expected response is
deterministic reconciliation, `NeedsScratchMedia`, `PlanInvalidated`, unsupported
media or read-only manual-recovery state. None permits guessing which copy or
sector image is authoritative.

### Assertions after recovery

The independent oracle must establish all of the following:

- Every protected/user file still has its expected logical bytes and metadata;
  unrelated names, directory structures and owned allocations are unchanged.
- The relocated file has its original logical length and full-file SHA-256.
  Success requires one **physical** extent, not merely aligned I/O.
- No live files share a cluster; no live cluster is marked free; old source
  allocations are released only after commit and verified cleanup.
- Free counts, allocation bitmap/FAT relationships and supported filesystem
  hints are consistent; no unowned leak remains except an explicitly retained
  recovery reservation with a valid owner record.
- Every raw write stayed inside its validated field/sector allowlist. An invalid
  plan, unsupported variant or wrong medium caused zero raw writes.
- Missing media or an unresolved journal prevents ordinary mount/publication.
  Cancellation/repeated recovery never deletes the last verified surviving copy.

Run an independent read-only filesystem checker **after recovery completes**,
then mount and hash files through an independent filesystem reader. Do not run a
repairing checker against deliberately interrupted intermediate metadata.

### Runtime and memory checks

Run the image-model suite under normal C builds, O3/LTO and address/undefined-
behavior sanitizers. Build the native interface for the actual 32-bit PowerPC
runtime and exercise error/result mapping in compiler/AOT tests. Inject short
I/O and failed sync/readback through the same adapter boundary hardware will use.

Use one 32 KiB aligned transfer buffer initially; never exceed the established
64 KiB transfer-scratch limit. Declare and measure the separately capped metadata
image pool, planning tables, journal buffers, stack usage and driver caches.
Increasing file/volume/cluster size must not introduce an ISO-sized allocation,
a full-volume bitmap or an uncapped cluster-sized buffer. Journal storage may
scale with the planned window or manifest, but its complete peak must be checked
before mutation.

### Physical acceptance and release evidence

For every proposed supported combination, record FAT32/exFAT variant, partition
layout, sector/cluster geometry, card model/capacity, interface, console,
driver/firmware version, full ISO SHA-256 and backup SHA-256. SD Gecko A/B, SD2SP2
and a demonstrably writable GC Loader require separate acceptance rows; passing
one does not establish another's capability.

Use only sacrificial media with verified backups. Cut power or remove media at
each authority transition, during journal replication, during sector writes and
during cleanup; include repeated cuts during recovery. Verify data and filesystem
invariants independently after every recovered case. Record throughput, elapsed
recovery time, resident memory and data/journal/metadata write amplification.
A manual interruption campaign is finite evidence, not an arbitrary-power-loss
guarantee.

Each acceptance record must link the exact commit, tool/specification versions,
fixture/manifest hashes, fault point or seed, expected and observed outcome,
recovery trace and reviewer. Release requires independent metadata/protocol
review and explicit hardware sign-off for the supported matrix. Missing rows,
unresolved recovery-discovery/dirty-state ordering, unexplained corruption or
unbounded memory keep P6 disabled.
