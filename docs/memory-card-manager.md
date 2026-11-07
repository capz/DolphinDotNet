# IPL-style memory card management

`Dolphin.Storage.MemoryCardManager.Open(StorageDevice.MemoryCardSlotA)` owns an
entire slot, independently of a game's identity. It cannot coexist with a
`MemoryCard` game-scoped mount in that slot. Call on the application executor and
dispose the manager when leaving the screen. libogc EXI attachment has a 300 ms
debounce interval; a newly attached card may initially be unavailable. Keep the
UI responsive and retry later rather than blocking its rendering loop.

`IsReady` distinguishes a mounted card from a broken/differently encoded card.
Opening never formats. `MountResult` retains libogc's result. Only an explicitly
confirmed `Format()` erases it. `Count`, `FreeBlocks`, `TotalBlocks` and
`FreeEntries` expose the directory/capacity snapshot. One block is 8192 bytes;
five system blocks are excluded. An empty directory is valid even though libogc
returns `CARD_ERROR_NOFILE` for it.

`ReadPage(start, maximum: 6)` retains only bounded 128-byte metadata snapshots,
with at most 16 saves per call. A save exposes ownership, title/comment, raw
filename, block count, attributes and icon timing. Display strings are byte-
preserving Latin-1 views, not Shift-JIS conversion. Native mutations preserve
all raw filename bytes. `ReadIcon(save, frame)` decodes one 32×32 RGBA8 RGB5A3 or
CI8 frame (4 KiB). A native sector cache shares the icon/palette read. No giant
managed save buffers are allocated. Refresh/removal invalidates snapshots;
select a fresh entry before mutation.

```csharp
using var source = MemoryCardManager.Open(StorageDevice.MemoryCardSlotA);
using var destination = MemoryCardManager.Open(StorageDevice.MemoryCardSlotB);
var save = source.ReadPage(0)[0];
// Show the save, destination and explicit Copy/Move confirmation first.
using var transfer = source.BeginCopy(save, destination, move: false);
while (!transfer.Complete)
{
    transfer.Step(); // At most one 8 KiB read/write or verification step.
    // Present another application frame and report transfer.Progress.
}
```

Copy and move permissions are independent (`NOCOPY=0x08`, `NOMOVE=0x10`). Existing
saves are never overwritten. A move removes the source only after complete CRC32
readback and metadata/attribute verification. Contents, banner/icon/comment
metadata and permissions are preserved; libogc updates modification time.
Each owned write refreshes that timestamp because `CARD_Write` changes it.
libogc also mutates `card_file.len`; bounds use immutable save metadata instead.

Dispose/cancel removes only an identifiable partial destination. If removal or
identity changes prevent safe cleanup, the source remains and the error tells
the caller to inspect the destination. `MemoryCardCommittedException` means the
mutation completed but subsequent directory refresh failed: reopen/refresh,
**do not repeat the destructive action**. Stale or failed snapshots cannot be
used for another operation. Bootstrap cancels outstanding transfers on exit.

The game-scoped adapter also now treats empty listings correctly and reads full
sectors instead of repeatedly submitting 512-byte slices.

## Validation

`card_manager_test` exercises this backend against a libogc boundary fixture,
including mutating I/O handles/timestamps, empty-card return codes, raw 32-byte
names, icon formats, protection, space, CRC corruption, cancellation, card
removal, explicit formatting and errors after commit. Run at default/O3-LTO and
with ASan/UBSan. `CardManagerSmoke` performs actual native card operations using
two fresh isolated Dolphin card files; it must never run on a physical user's
cards. Physical-console/hot-removal/power-loss acceptance remains separate.
