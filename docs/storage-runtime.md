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

SD mounts register VFAT/exFAT and select the first mountable partition, with four cache pages of eight sectors (about 16 KiB per device). Mount returns false when no supported medium/filesystem can mount; it never formats media. ISO9660 uses its own pathname limits. `Storage.Mount(DvdDrive)` retains ISO9660 behavior. Use `Storage.MountDisc(DiscFormat.GameCubeFst)` for original GameCube discs, or `MountDisc(DiscFormat.Iso9660)` explicitly; unmount before changing formats. FST validates the disc magic, entry tree, names and data ranges and exposes files/directories through a read-only devoptab. GC Loader block filesystems remain outside this backend. Memory-card enums cannot be passed to Storage.GetPath or ordinary Directory operations.

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

WriteAllBytes stores a logical payload behind a versioned big-endian header; sector padding is omitted by ReadAllBytes. WriteSave adds two 32-byte UTF-8 comment fields (31 text bytes maximum each), an optional 96×32 tiled RGB5A3 Banner (6144 bytes), and an optional 32×32 tiled RGB5A3 Icon (2048 bytes), and sets native CARD status metadata. Use the desktop [SaveImage converter](../tools/DolphinDotNet.SaveImage/README.md) to resize PNG assets and encode tiled RGB5A3 icons/banners. Animated icons are not supplied. Title/Comment are storage metadata; no UI is added.

Native CARD reads use aligned 512-byte transfers and writes use aligned card sectors. Each mounted slot needs a 40 KiB native work area, separate from the managed heap. Container construction and transaction staging temporarily allocate native memory.

Replacement, including physical allocation resizing, first writes and verifies an immutable `.__dnd_transaction` recovery record containing the target name, complete container, status flags and checksums. The previous save remains untouched until staging succeeds. The backend then deletes/recreates the target, writes its data and status metadata, and finally removes the journal. Interrupted replacement is completed at mount and before the next operation. Failed staging preserves the old save; failed commit preserves the verified new record. After readback verification, a durable CARD directory status update marks the journal ready before replacement starts. Unmarked staging records are discarded; malformed marked records fail recovery and retain the journal. This protocol assumes libogc CARD directory recovery and sector writes work as specified; arbitrary media corruption is not repairable by this journal.

The journal is reserved and never appears as a logical save. Replacement needs enough free space and an extra directory entry to stage the new save while retaining the old allocation. Full/removable media can prevent completion; the journal remains for retry. Formatting and general corrupted-save repair are outside this API.

## Cooperative async

Tasks and compiled async/await run on one application thread. `Task.Run`, completion sources, Yield, WhenAll/WhenAny, awaiters/builders and cancellation feed `Dolphin.Threading.Tasks.Scheduler`. Call `Scheduler.Pump()` per frame or RunUntilIdle to advance queued work; waiting/GetResult also pumps until completion. A wait with no runnable completion path throws rather than spinning forever. ConfigureAwait has no synchronization context to capture.

Stream/file writes and copies schedule bounded transfers (up to 4 KiB) between queue turns; text operations use bounded character chunks. Cancellation is checked before opening and between chunks, and faults/cancellation are stored in the returned task. Native descriptor operations remain synchronous within each chunk: this is cooperative scheduling, not a thread pool or interrupt-driven descriptor API. Concurrent operations on the same stream must be serialized by the application. Dispose streams only after their pending operations complete. Thread, locks, timers, async void, ValueTask and full TPL scheduling/context behavior are not supplied.

UTF codecs preserve surrogate pairs across writer buffers and reader byte boundaries. Strict UTF-8/UTF-16 operations throw encoder/decoder fallback exceptions; permissive operations replace malformed sequences. UTF-32 and arbitrary code pages are unsupported. Text APIs are bounded by the managed heap; use streams for large content.

## Verification

CI runs the compiled smoke on 32/64-bit hosts, optimized ASan/UBSan, injected short I/O and failure cleanup, and GC stress, alongside the existing compiler/runtime regressions and GameCube DOL build. Library and storage smokes cover deferred tasks, cancellation, text boundaries and GC roots. The portable FST fixture rejects malformed trees/extents; the native-card transaction fault matrix retries failures at allocation, sector write, close, delete and metadata stages while checking complete old/new data. Host fixtures test API behavior, not physical hardware. The target build compiles the storage drivers but does not execute mounts or prove FAT encoding, ISO9660 media compatibility, card removal, DMA behavior or save-browser rendering. These remain target acceptance checks.
