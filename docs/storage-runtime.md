# Managed collections and storage

This is a compiled collections, text, cooperative async and storage API subset, not complete System.IO or .NET Standard conformance. `tests/StorageSmoke` executes ordinary System.IO calls after AOT; the host harness supplies temporary mounted volumes. GameCube storage drivers build against the pinned libogc2 toolchain. Actual media, removal, Unicode lookup and save-browser presentation still require target testing.

## Collections

Ordinary `System.Collections.Generic.List<T>` calls map to `Dolphin.Collections.List<T>`. Constructors accept capacity or `IEnumerable<T>`, including arrays. The list supports generic collection/read-only interfaces, indexing and capacity, insertion/removal/ranges, copies, searches and predicates, reverse, comparer/delegate sorting and binary search. Self-AddRange/InsertRange snapshot the source. Concrete enumerators are value copies; generic Current returns default before/after enumeration, nongeneric Current throws, and mutation invalidates MoveNext/Reset. Nongeneric IList, LINQ and Dictionary are outside this subset.

`EqualityComparer<T>.Default` and `Comparer<T>.Default` support integer widths/signedness, floating-point NaN/zero semantics, nullable values, strings, custom typed contracts and field-based value equality/hash fallback. Reference identity is the fallback when no equality contract/override applies; unsupported ordering throws ArgumentException. Hashes are implementation-defined and must not be persisted. `Comparer<T>.Create` and custom comparer interfaces are supported. `StringComparer.Ordinal` and `OrdinalIgnoreCase` use Unicode 16 simple uppercase data; culture-based comparers/globalization are unavailable. Default string ordering is ordinal on this console runtime.

Struct parameters/returns, boxing, aggregate arrays and embedded GC references are covered by the library smoke. This is not a promise that every framework value type or interface specialization is available.

## Binary files and directories

The compiler redirects supported ordinary System.IO calls to managed `Dolphin.IO` algorithms. Applications must reference DolphinDotNet.Core. Parameter signatures select overloads; unsupported overloads fail compilation.

| Type | Implemented overloads |
| --- | --- |
| Path | Combine(string,string), GetFullPath(string), IsPathRooted(string), GetFileName(string), GetDirectoryName(string), GetExtension(string) |
| File | Exists, ReadAllBytes/WriteAllBytes, ReadAllText/WriteAllText/AppendAllText, ReadAllLines/WriteAllLines/AppendAllLines, lazy ReadLines (encoding overloads), cooperative async byte/text/line operations (encoding/cancellation overloads), Open(path,mode[,access[,share]]), OpenRead, OpenWrite, Create(path), Delete, Move(source,destination), Copy(source,destination[,overwrite]) |
| FileStream | Constructors(path,mode[,access[,share]]), Stream inheritance, CanRead/CanWrite/CanSeek, Length/Position, Read/Write(byte[],offset,count), Seek, SetLength, Flush, Close/Dispose, inherited async/copy operations |
| Directory | Exists, CreateDirectory, Delete(path[,recursive]), Move, Get/SetCurrentDirectory, GetFiles/GetDirectories/GetFileSystemEntries(path), EnumerateFiles/EnumerateDirectories/EnumerateFileSystemEntries(path), all with pattern and SearchOption overloads |
| MemoryStream | Empty/capacity/byte-array/segment constructors, Position/Length/Capacity, fixed/expandable and writable/exposable modes, ToArray/GetBuffer/WriteTo, Stream operations |
| Text | UTF-8, UTF-16 LE/BE, ASCII and Latin-1 Encoding; StreamReader/StreamWriter, BOM detection/emission, line/character reads/writes, buffering, leaveOpen and async methods |
| DirectoryInfo | Constructor(path), FullName, Name, Exists, Create, Delete([recursive]) |

Paths use `volume:/relative/path`; slash and backslash normalize, leading slash selects the current volume, and dot segments cannot climb above a volume root. Relative paths initially use `sd:/`; SetCurrentDirectory can select another mounted directory. Empty paths, invalid UTF-16, embedded NUL and unknown volume prefixes are rejected. This is a console dialect, not Windows/UNC emulation.

Read/Write/ReadWrite sharing is enforced in a process-local registry, with case-insensitive path comparisons. Delete sharing, FileStream options/buffering constructor overloads, timestamps, attributes, symlinks, FileInfo and DriveInfo are outside this slice. Patterns support `*` and `?` against filenames, with ordinal case-sensitive matching; `*.*` matches all names. SearchOption.AllDirectories uses lazy traversal and deterministic cursor cleanup. Directory order is unspecified. Recursive deletion refuses volume roots and bounds depth to 64. Streams/cursors require deterministic disposal; unmount refuses active handles. Directory enumeration opens a cursor at GetEnumerator and releases it at EOF, Dispose, early foreach exit or exceptions. No managed threading is available; these tables and card identities assume serial operations.

The backend owns 32 file and 32 directory slots with non-reused handle tokens, 1024-byte path buffers and 255-byte component limits. Descriptor transfers use 4 KiB aligned staging buffers. Offsets are checked against native off_t; managed long does not guarantee arbitrary native file sizes. ReadAllBytes is bounded by the managed heap. Copy uses a 4 KiB managed buffer and removes a new partial destination on failure. Cross-volume file moves copy then delete and are not atomic; directory moves across volumes are unsupported. Ordinary file writes are not power-loss atomic. Native save replacement uses the recovery transaction described below.

## Mounted devices

```csharp
using Dolphin.Storage;
using System.IO;

if (Storage.Mount(StorageDevice.SdSerialPort2))
{
    Directory.CreateDirectory(Storage.GetPath(StorageDevice.SdSerialPort2, "saves"));
    Storage.WriteAllBytes(StorageDevice.SdSerialPort2, "saves/demo.bin", bytes);
    foreach (var path in Storage.EnumerateFiles(StorageDevice.SdSerialPort2, "saves"))
        Consume(path);
    Storage.Unmount(StorageDevice.SdSerialPort2);
}
```

| Enum | Prefix/backend |
| --- | --- |
| SdSlotA | carda:/, SD adapter in slot A |
| SdSlotB | cardb:/, SD adapter in slot B |
| SdSerialPort2 | sd:/, SD2SP2 |
| DvdDrive | dvd:/, physical DVD interface with read-only ISO9660 or original-disc FST |
| MemoryCardSlotA / MemoryCardSlotB | Separate MemoryCard API; no filesystem path |
| GcLoader | gcloader:/ (GC Loader SD via libogc2 `__io_gcode`, when firmware exposes sectors) |

SD mounts register VFAT/exFAT and select the first mountable partition, with four cache pages of eight sectors (about 16 KiB per device). Mount returns false when no supported medium/filesystem can mount; it never formats media. ISO9660 uses its own pathname limits. `Storage.Mount(DvdDrive)` retains ISO9660 behavior. Use `Storage.MountDisc(DiscFormat.Fst)` for original GameCube discs, or `MountDisc(DiscFormat.Iso9660)` explicitly; unmount before changing formats. FST validates the disc magic, entry tree, names and data ranges and exposes files/directories through a read-only devoptab. GC Loader block filesystems remain outside this backend. Memory-card enums cannot be passed to Storage.GetPath or ordinary Directory operations.

## GC Loader and unified GameCube DVD filesystems

All original GameCube FST content is mounted as `dvd:/`, regardless of source.
`Storage.GetMountedDiscIdentity([language])` returns the cached validated boot header
(GameId, disc number, revision and internal title), and reads `dvd:/opening.bnr`
when present to expose the displayed title, short description, publisher and
96x32 banner. For BNR2, the language is selectable (English, German, French,
Spanish, Italian or Dutch). `DiscIdentity.Banner.Rgba32` contains linear RGBA
pixels ready for `Texture2D.Create`; `Rgb5A3` preserves original GX tiles.
An absent or malformed banner leaves `Banner` null and falls back to the boot
header title. Japanese BNR1 text can be Shift-JIS; the raw banner bytes are
preserved but display strings currently use western Latin-1 decoding.
**The full image path is the only API difference.** The same `Storage` path,
directory and stream operations are used after either mount:

```csharp
// Original game disc or ODE-selected disc:
if (Storage.MountDisc()) {
    string[] names = Storage.GetFiles(StorageDevice.DvdDrive, "");
    Storage.Unmount(StorageDevice.DvdDrive);
}
// ISO/GCM stored on a previously mounted SD, SD2SP2 or GC Loader volume:
if (Storage.MountDisc("sd:/games/game.iso")) {
    byte[] data = Storage.ReadAllBytes(StorageDevice.DvdDrive, "opening.bnr");
    Storage.Unmount(StorageDevice.DvdDrive);
}
```

Both routes use the existing `dnd_fst` validation, read-only `dvd:/` devoptab,
file streams, path normalization, stat, and directory enumeration. The
`Storage.MountDisc(DiscFormat.Iso9660)` overload retains its separate meaning
for an ISO9660 data disc. There is **no separate public ISO-file browsing API**.

An ISO image is read in place without extraction. It requires a full
volume-qualified path and a mounted, seekable storage source. Unmount `dvd:/`
before unmounting that source; the runtime rejects an earlier source unmount.
Only one `dvd:/` mount can exist at a time; close any `dvd:/` streams before
unmounting. Original GameCube ISO/GCM format only (not compressed RVZ/CISO,
Wii partition images or ISO9660-in-ISO). Native 32-bit `long` limits individual
image files to below 2 GiB.

GC Loader's optional raw SD access remains
`Storage.Mount(StorageDevice.GcLoader)`, available at `gcloader:/` through
libogc2 `__io_gcode` if supported by the installed GC Loader firmware.
Availability and simultaneous ODE DVD use are hardware dependent; do not
assume they can operate simultaneously. See [DiscBrowser](../samples/DiscBrowser/README.md).

`Storage.SelectGcLoaderDiscImage("gcloader:/games/new.iso")` changes GC Loader's
physical DVD mapping; `MountDisc(imagePath)` only presents an image through the
SDK's filesystem. Physical selection requires an already-mounted GC Loader raw
volume, `dvd:/` unmounted, and all writable streams and contiguous writers closed.
It accepts a nonempty, completely contiguous FAT32/exFAT file, resolves its
absolute SD sectors (including the partition once), checks protocol bounds and
GameCube magic, then sends the GC Loader fragment-table/select commands through
libogc2's bounded DVD queue. It returns only after the physical DVD interface
matches the image's entire `0x440`-byte boot header. It does not allocate, rename,
delete or overwrite any image file. Hosts and unsupported media throw.

For an updater, first verify the whole new download and close its streams, call
`SelectGcLoaderDiscImage`, then delete the old image using the existing mounted
raw volume, unmount that volume and hand off to the launcher. Keep the selected
image unchanged. On **any** selection/readback failure retain both images and
abort cleanup: a libogc2 timeout can reset the drive, so the old mapping cannot
be promised after failure. Do not reset/remount GC Loader between selection and
launch; its raw-device startup resets the drive. Physical GC Loader acceptance
is separate from host protocol and fault-injection fixtures.

GameCube native bundles rebuild the pinned libdvm/FatFs FAT drivers with an
exFAT path bound of 64 directory components. The toolchain's original bound was
10: following the eleventh directory returned `FR_NOT_ENOUGH_CORE`, which its
driver translated into `ENOMEM` and therefore `OutOfMemoryException`, despite
using stack scratch buffers and having available RAM. This could stop a recursive
volume scan as soon as it reached an unrelated deep directory.

The SDK raises that bound and translates an over-depth path into
`ENAMETOOLONG` / `PathTooLongException`. FAT/FAT32 traversal is unaffected.
All FatFs code and its libdvm driver are rebuilt together because `FATFS` embeds
the path tables; mixing the enlarged private structure with an old `ff.o` or
`fat_driver.o` would corrupt its layout. The build emits matching local
`libfat.a` and `libdvm.a` archives plus pinned-source fingerprints, without
modifying the installed toolchain or card contents. The larger path tables cost
1,296 additional native bytes per mounted FAT volume.

## Discovering and restoring mounts

All high-level filesystem mount routes now share a session-local native registry:
SD adapters in slots A/B, SD2SP2, physical/ODE discs, image-backed `dvd:/`, and
GC Loader's raw filesystem. Memory-card saves and `MemoryCardManager` are excluded.
Existing `Storage.Mount`, `MountDisc`, `Unmount`, `IsMounted` and `GetPath` signatures
remain compatible and automatically update the registry. No application-side
registration is necessary, even if the mount was made by another library.

```csharp
MountInfo[] devices = Storage.GetMounts(); // Five entries, including never-mounted devices.
MountInfo? last = Storage.GetLastMount(); // null until a mount succeeds.
if (last != null) {
    if (!Storage.TryGetMountedFileSystem(last.Device, out var fs)) {
        var restored = Storage.Remount(last.Device);
        if (!restored.Succeeded) return; // Inspect restored.Failure.
        fs = restored.FileSystem;
    }
    string root = fs!.RootPath;
    using var input = fs.Open("opening.bnr", FileMode.Open, FileAccess.Read);
}
```

`GetMountInfo(device)` is an immutable observation containing `State`, `Presence`,
`RootPath`, `IsReadOnly`, `MountId`, `LastMountSequence`, `ChangeSequence`,
`SourceMountId`, `LastNativeError` and `LastSuccessfulConfiguration`. Queries do
not probe, reset, mount or write devices. State is `NeverMounted`, `Mounted`,
`Unmounted`, or `Unavailable`; presence is a separate last-observed value.
`IsReadOnly` describes the SDK's filesystem policy (DVD is read only), not a
physical-media writeability guarantee. Mount snapshots do not contain native pointers.

"Last" means the most recent **successful** filesystem mount anywhere in this
application run, even if it has since been unmounted or removed. Failed attempts,
reusing an active mount and explicit unmounts do not reorder it. Mounting a missing
ISO dependency is itself a successful source-volume mount and is recorded too.
State resets when a new application starts; nothing is persisted to disk.

Configurations survive unmounts, removal and failed remounts. Block-volume
snapshots contain the selected partition LBA, detected FAT/exFAT driver, and cache
settings. Input `PartitionLba = -1` / `FileSystemFormat.Automatic` requests the
legacy selection policy; successful GameCube snapshots contain the effective
selection for exact replay. Cache pages (1..64) and sectors per page (1..128)
must be powers of two. The defaults remain four pages of eight sectors.
Disc snapshots retain their actual format: `Mount(DvdDrive)` remains ISO9660,
whereas `MountDisc()` remains FST. Image snapshots contain a normalized full image
path and the source volume's configuration.

```csharp
var configuration = MountConfiguration.BlockVolume(StorageDevice.SdSerialPort2);
MountResult mounted = Storage.Mount(configuration);
MountConfiguration image = MountConfiguration.DiscImage("sd:/games/game.iso",
    mounted.FileSystem!.Info.LastSuccessfulConfiguration);
MountResult dvd = Storage.Mount(image);
```

`Mount(configuration)` and `Remount(device)` report `MountFailure` without
replacing a different active configuration. An exact active match returns borrowed
access without mounting again. `Remount` returns `NoPreviousConfiguration` for a
device that has never mounted successfully. Image restoration reuses a matching
source or mounts its remembered source first. A conflicting source returns
`ConfigurationConflict`. If image mounting fails, the runtime attempts to unmount
only a source acquired by that attempt; a backend cleanup failure leaves that
source visible in the registry. Existing sources are not unmounted on failure.
Invalid factory arguments still throw ordinary argument exceptions.

`MountedFileSystem` is borrowed, not an owning/disposable mount lease. Its
`IsValid` and generation-checked `Open` refer to one mount lifetime. All streams,
including those opened through old APIs, and directory enumerators are invalidated
when removal is observed. Restoring the same path creates a new `MountId`; old
access cannot become valid again. Disposal of invalidated streams/iterators still
releases their handle slots. `RootPath` and strings returned by `GetPath` remain
ordinary paths: later path-based I/O addresses the current occupant of that path.
Use `fs.Open` when generation identity matters.

Call `Storage.Poll()` once per application update to observe removal and dispatch
`Storage.MountChanged`. Callbacks run on the application executor, after native
state changes and outside storage operations. Native I/O failures also trigger
non-resetting checks; `Busy`/`Unknown` observations do not mean absent. Detection
is observational, not instantaneous hardware interrupt notification. A removed
source invalidates its image-backed DVD as well. A gate beneath the block cache
prevents removed-volume cleanup from writing buffered data to replacement media;
unfinished journals are left for validated recovery on a later mount.

Changes have `Reason`, `Info` and `Sequence`. The queue retains up to 16 changes.
If it overflows, the next delivery is `HistoryLost` with null `Info`: pending old
changes are dropped and callers should resynchronize through `GetMounts()`.
Callbacks may subscribe/unsubscribe or query storage. Reentrant `Poll` calls are
ignored and one call dispatches at most 17 records, so callback-driven transitions
cannot create an unbounded dispatch loop. Subscriber exceptions propagate after
the transition commits and the consumed change is not replayed.

Restoring settings does **not** prove that an SD card or disc is the identical
physical medium. Partition/format/path conflicts are checked; stable physical-media
identity and replacement between observations cannot be guaranteed by these
backends. Applications requiring content identity should verify their manifest,
hash or `GetMountedDiscIdentity()` after restoration. GC Loader probe reliability
and simultaneous raw-SD/DVD access remain firmware dependent.

Native callers use the equivalent declarations in `include/dnd_storage.h`:
`dnd_storage_get_mount_info`, `dnd_storage_last_mount_device`,
`dnd_storage_mount_configuration`, `dnd_storage_remount`,
`dnd_storage_mount_valid`, `dnd_storage_open_mounted`, `dnd_storage_poll` and
`dnd_storage_next_mount_change`. `DndMountInfo.root_path` is available even after
unmount; native polling queues changes but never invokes managed callbacks.
The registry uses fixed-size records and a bounded queue, with no heap allocation
for native queries or generation checks. The managed wire format is private,
versioned and independent of native struct padding and host endianness.

Run `python3 tests/storage_mounts.py` with .NET 8+ and a C compiler for native
host/fake-GameCube lifecycle tests (including O3/LTO), removal-gate checks,
real AOT managed tests with GC stress, and existing storage/fault regressions.
The storage CI domain runs this suite. Physical hotplug/writeback behavior still
requires console validation.

## Contiguous file creation (GameCube FAT32 and exFAT)

Use `Storage.CreateContiguousFile(device, path, knownLength)` when writing a new
large file whose length is known before downloading or generating it:

```csharp
using (var writer = Storage.CreateContiguousFile(StorageDevice.SdSerialPort2,
        "downloads/package.dol", contentLength))
{
    // Stream chunks rather than allocating the whole file in managed memory.
    writer.Write(buffer, 0, bytesRead);
    // Repeat until exactly contentLength bytes have been written.
    writer.Complete(); // Verifies on-media checksum and publishes the path.
}
```

The writer creates an exclusive staging file on the same mounted volume,
preallocates its full size through libdvm's existing ftruncate / FatFs
f_expand path, flushes allocation metadata, **inspects the physical FAT32 or
exFAT cluster chain**, and starts accepting data only when the reservation is
one contiguous extent. libdvm ignores the f_expand return code, so checking
the chain is necessary. Data transfers use an aligned 16-KiB native staging
buffer, then the complete file is synced and read back for a checksum before
it is renamed into place. `Complete()` must be called explicitly. Disposing
without it aborts the transaction. Zero-length files need zero allocated
extents. Individual file lengths are currently limited to 2,147,483,647 bytes.

**Safety and limitations:** This is create-new, not atomic replacement. Existing
destination files are never overwritten. A small journal identifies abandoned
staging files so a subsequent `Storage.Mount` can remove them after a crash
or power loss. Corrupt journals fail closed rather than deleting unknown
files. `Unmount` refuses a volume with a live writer, and ISO-backed SD
volumes cannot be modified while `dvd:/` is mounted from them. No FAT metadata
is mutated directly by the extent scanner. A successfully returned `Complete`
means the data and FAT allocation were checked, **not** that a FAT directory
rename is guaranteed power-loss atomic on every card/firmware combination.
Applications should keep authoritative download hashes/version manifests.

For a download UI, use `await writer.CompleteAsync(cancellationToken)` instead
of `Complete()`. Readback advances by at most 16 KiB per step and yields to the
application scheduler between steps, allowing rendering and cancellation to
continue. Cancellation or failure discards the staged file; publication happens
only after the same length, checksum and physical-extent checks as `Complete()`.
Cancellation after successful publication cannot undo the published file.
Do not write or start another completion on that writer while its completion
task is pending. Disposing it aborts the pending verification. Metadata flush,
extent inspection and the final rename remain synchronous native operations.
Temporary and journal files occupy root-directory entries until publication.
Only one contiguous writer can be active per volume. On host filesystems
the feature fails as unsupported rather than claiming unverified contiguity.
For GC Loader, raw SD mountability and firmware write support must be checked
on the target console. Benchmarks and power-cut tests on real cards remain
mandatory.

## Hardware acceptance

The new [headless StorageIntegrity probe](../samples/StorageIntegrity/README.md)
benchmarks SD2SP2 and writable GC Loader volumes using 1 MiB writes and
16/64 KiB buffers. Its opt-in `POWER` mode continuously exercises
checksum-verified, journaled create-new writes and checks publication after
reboot. It is **not** run by CI on actual devices; the CI job compiles the
sample and separately runs deterministic native checkpoint-injection tests.
Use a separate test card, measure FAT32 and exFAT, near-full media, and log
firmware/card details before promoting the feature to hardware-verified.

## Explicit maintenance, policy and streaming

Opted-in writes preserve their existing .NET APIs. Advanced callers may also
inspect or repair existing files on mounted FAT32/exFAT GameCube SD volumes:

```csharp
using Dolphin.IO;

var before = Defrag.Analyze("sd:/games/game.iso");
var result = Defrag.Optimize("sd:/games/game.iso");
Console.WriteLine(result.Outcome + " " + result.After.ExtentCount);
```

`Optimize` runs independently of `OptIn` and returns an outcome of
`AlreadyContiguous`, `Optimized`, `NoContiguousSpace` or `Unsupported`.
The result reports physical extent counts before/after, copied bytes and elapsed
milliseconds. For automatic writes, `Defrag.GetStatistics()` reports
attempts, successful reservations and repairs, no-space/unsupported skips,
failures, total bytes written/copied and the last outcome; call
`Defrag.ResetStatistics()` to clear these process-local counters. Automatic policy remains disabled by default; `MinimumFileSize`
defaults to 65,536 bytes and controls **automatic** optimization only. A scoped
policy restores the previous process-wide setting:

```csharp
using (Defrag.BeginScope(true, 131072)) {
    // Your existing managed filesystem writes.
}
```

Scopes must not overlap across independent application threads. Large,
known-length downloads can avoid allocating their entire payload and bypass
post-write copying:

```csharp
using var input = File.OpenRead("sd:/download/source.bin");
Defrag.WriteContiguousFrom(input, "sd:/games/new-game.bin", input.Length);
```

`WriteContiguousFrom` uses a fixed 16 KiB managed buffer, requires the target
not to exist and publishes only after readback checks. It throws rather than
silently accepting fragmentation or an incomplete input stream. The policy and
manual APIs do not implement a whole-card defragmenter. The physical-name
scanner handles valid UTF-8 paths and FAT32/exFAT UTF-16 names, including
surrogate pairs. Non-ASCII names currently require the same Unicode spelling
and casing as their stored name (ASCII casing is folded). Unicode canonical
normalization and locale-specific case folding are not performed.

## Automatic fragmentation prevention and repair

**Disabled by default.** Before starting the writes you want optimized, call
`Dolphin.IO.Defrag.OptIn(true)`. Disable the behavior again with
`Dolphin.IO.Defrag.OptIn(false)`, and read its current process-wide setting
through `Defrag.IsEnabled`. The setting is reset to off on a fresh runtime
launch. It gates both new-file contiguous reservation and the post-write
repair path; explicit calls to `Storage.CreateContiguousFile` remain available
regardless of the setting. A stream's final close uses the setting active at
close time; enable it before starting a multi-step write and leave it enabled
through completion. Recovery of earlier interrupted operations still runs at
mount even while the policy is off.

```csharp
using Dolphin.IO;

Defrag.OptIn(true);
try
{
    File.WriteAllBytes("sd:/downloads/package.dol", package);
    // FileStream, File.Copy, text and async writes share the policy.
}
finally
{
    Defrag.OptIn(false);
}
```

When opted in, the managed file-writing APIs use both strategies on the
GameCube's mounted FAT32/exFAT SD volumes (carda:/, cardb:/, sd:/, or
gcloader:/ where firmware exposes writable sectors).

- **Known-length new writes:** `File.WriteAllBytes` / `Storage.WriteAllBytes`
  attempt checked, single-extent preallocation and checksum-verified staging
  before publishing files of at least **64 KiB**. If a contiguous region
  cannot be reserved, the normal write path remains available.
- **Managed streams, appends, copies, text and async writes:** FileStream
  tracks modifications; after the final handle closes, only files at least
  **64 KiB** are inspected for fragmentation. Single-extent files are
  immediately skipped. If fragmented, a verified contiguous temporary copy
  is staged and swapped under a recovery journal. `File.Copy` and nonappend
  byte-array async writes also communicate the known length to the allocator.
- **Safe fallback:** Lack of contiguous free space, unsupported/non-ASCII
  filesystem metadata or insufficient space for an entire temporary copy
  leave the file untouched rather than failing an otherwise successful write.
  Files sharing an active handle or backing mounted DVD images are skipped.
  Original contents are retained in a recoverable backup during replacement.
  Failed recovery is surfaced as a storage I/O error.
- **No whole-volume scan:** There is no scan/rewrite of unrelated SD files.
  Small files and rename-only moves do not trigger a maintenance pass.
  This affects only files written by the application, not all files already
  on the card. An API for explicit large-file maintenance is a possible
  separate feature.

The post-write repair is synchronous and can require a full file copy.
It is therefore unsuitable for latency-critical, frequently-written large
files unless the application schedules those writes appropriately.
Ordinary filesystem writes and renames are not guaranteed power-loss atomic;
a safe unmount and real-device interrupted-power tests remain necessary.

## Native memory-card saves

```csharp
using (var card = MemoryCard.Mount(StorageDevice.MemoryCardSlotA,
           new MemoryCardIdentity("DDNT", "01")))
{
    var options = new MemoryCardSaveOptions { Title = "DolphinDotNet", Comment = "Slot one" };
    card.WriteSave("Save01", bytes, options);
    foreach (var entry in card.GetEntries()) Consume(entry.Name, entry.Length);
    var restored = card.ReadAllBytes("Save01");
}
```

Game/company codes are four/two uppercase alphanumeric characters. Save names are 1–32 ASCII letters/digits/underscore/hyphen; saves have no hierarchy. GetEntries materializes MemoryCardEntry objects with Name and logical Length for Dolphin save containers in that identity. Foreign or corrupt containers report IOException rather than guessing payload lengths. Multiple owners may share a slot only with the same identity; each Dispose releases one mount reference.

For slot A/B insertion/removal, use `Dolphin.Hardware.MemoryCards.Update()` and
`GetState(MemoryCardSlot.A/B)` from the GameCube library; see
[connection snapshots](API_REFERENCE.md#dolphinhardwarememorycards--slot-connection-snapshots).
This monitors physical presence independently of mounted handles. Busy/unknown
probes do not synthesize unplug events. It never mounts or modifies a card.

WriteAllBytes stores a logical payload behind a versioned big-endian header; sector padding is omitted by ReadAllBytes. WriteSave adds two 32-byte UTF-8 comment fields (31 text bytes maximum each), an optional 96×32 tiled RGB5A3 Banner (6144 bytes), and an optional 32×32 tiled RGB5A3 Icon (2048 bytes), and sets native CARD status metadata. Use the desktop [SaveImage converter](../tools/DolphinDotNet.SaveImage/README.md) to resize PNG assets and encode tiled RGB5A3 icons/banners. Pass `--source` to compile the converted bytes directly into the game as a C# property. `--disc-banner` separately creates `opening.bnr` for the root of a packaged disc filesystem. Animated icons are not supplied. Title/Comment are storage metadata; no UI is added.

Native CARD reads use aligned 512-byte transfers and writes use aligned card sectors. Each mounted slot needs a 40 KiB native work area, separate from the managed heap. Container construction and transaction staging temporarily allocate native memory.

Replacement, including physical allocation resizing, first writes and verifies an immutable `.__dnd_transaction` recovery record containing the target name, complete container, status flags and checksums. The previous save remains untouched until staging succeeds. The backend then deletes/recreates the target, writes its data and status metadata, and finally removes the journal. Interrupted replacement is completed at mount and before the next operation. Failed staging preserves the old save; failed commit preserves the verified new record. After readback verification, a durable CARD directory status update marks the journal ready before replacement starts. Unmarked staging records are discarded; malformed marked records fail recovery and retain the journal. This protocol assumes libogc CARD directory recovery and sector writes work as specified; arbitrary media corruption is not repairable by this journal.

The journal is reserved and never appears as a logical save. Replacement needs enough free space and an extra directory entry to stage the new save while retaining the old allocation. Full/removable media can prevent completion; the journal remains for retry. Formatting and general corrupted-save repair are outside this API.

## Cooperative async

See [async runtime and namespace rules](async-runtime.md) for the exact Task subset and a complete pump-driven networking example. Use `System.IO`/`System.Threading.Tasks` for supported standard calls and an alias for `Dolphin.Threading.Tasks.Scheduler`.

Tasks and compiled async/await run on one application thread. `Task.Run`, completion sources, Yield, WhenAll/WhenAny, awaiters/builders and cancellation feed `Dolphin.Threading.Tasks.Scheduler`. Call `Scheduler.Pump()` per frame or RunUntilIdle to advance queued work; waiting/GetResult also pumps until completion. A wait with no runnable completion path throws rather than spinning forever. ConfigureAwait has no synchronization context to capture.

Stream/file writes and copies schedule bounded transfers (up to 4 KiB) between queue turns; text operations use bounded character chunks. Cancellation is checked before opening and between chunks, and faults/cancellation are stored in the returned task. Native descriptor operations remain synchronous within each chunk: this is cooperative scheduling, not a thread pool or interrupt-driven descriptor API. Concurrent operations on the same stream must be serialized by the application. Dispose streams only after their pending operations complete. Thread, locks, timers, async void, ValueTask and full TPL scheduling/context behavior are not supplied.

UTF codecs preserve surrogate pairs across writer buffers and reader byte boundaries. Strict UTF-8/UTF-16 operations throw encoder/decoder fallback exceptions; permissive operations replace malformed sequences. UTF-32 and arbitrary code pages are unsupported. Text APIs are bounded by the managed heap; use streams for large content.

## Verification

CI runs the compiled smoke on 32/64-bit hosts, optimized ASan/UBSan, injected short I/O and failure cleanup, and GC stress, alongside the existing compiler/runtime regressions and GameCube DOL build. Library and storage smokes cover deferred tasks, cancellation, text boundaries and GC roots. The portable FST fixture rejects malformed trees/extents; the native-card transaction fault matrix retries failures at allocation, sector write, close, delete and metadata stages while checking complete old/new data. Host fixtures test API behavior, not physical hardware. The target build compiles the storage drivers but does not execute mounts or prove FAT encoding, ISO9660 media compatibility, card removal, DMA behavior or save-browser rendering. These remain target acceptance checks.

## Booting updated images

`Dolphin.IO.DiscLauncher.PrepareWithSwiss(swissDolPath, imagePath)` stages an
installed, argv-capable Swiss DOL and sets its `Autoload` argument. Pass the
returned `PreparedDol` to `DolLauncher.RequestLaunch`, dispose/unmount your
resources, then return successfully from Main. Prepare before committing an
update, so missing/unsupported Swiss binaries cannot strand the replacement.
SDK volume prefixes map to Swiss (`gcloader:/` → `gcldr:/`, `carda:/` → `sda:/`,
`cardb:/` → `sdb:/`, `sd:/` → `sdc:/`). Canonical ISO/GCM paths are required.
Swiss is an external prerequisite, not bundled by this SDK. It handles NKit,
ODE selection and game patches; extracting an ISO's DOL alone does not set up
its disc or boot environment. Test the installed Swiss and GC Loader firmware
on hardware; host path tests do not establish a successful console handoff.
