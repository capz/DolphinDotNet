# Managed collections and storage

This is an initial compiled API slice, not complete System.IO or .NET Standard conformance. `tests/StorageSmoke` executes ordinary System.IO calls after AOT; the host harness supplies temporary mounted volumes. GameCube storage drivers build against the pinned libogc2 toolchain. Actual media, removal, Unicode lookup and save-browser presentation still require target testing.

## Collections

`Dolphin.Collections.List<T>` provides growable storage, indexing, Count, Add/Clear/Insert/Remove/RemoveAt, Contains/IndexOf, CopyTo/ToArray and construction from IEnumerable<T>. It implements IList<T> and IReadOnlyList<T>, including generic/nongeneric enumeration and modification detection. Tested payloads are Int64 and strings; equality currently uses supported object equality (primitive boxes, string content and reference identity). Arbitrary value-type equality and custom comparers are not established. `ArrayEnumerable<T>` supplies an explicit enumerable adapter for one-dimensional arrays. Standard List<T>, LINQ and direct array-to-IEnumerable enumeration remain follow-up work.

## Binary files and directories

The compiler redirects supported ordinary System.IO calls to managed `Dolphin.IO` algorithms. Applications must reference DolphinDotNet.Core. Parameter signatures select overloads; unsupported overloads fail compilation.

| Type | Implemented overloads |
| --- | --- |
| Path | Combine(string,string), GetFullPath(string), IsPathRooted(string), GetFileName(string), GetDirectoryName(string), GetExtension(string) |
| File | Exists, ReadAllBytes, WriteAllBytes, Open(path,mode[,access[,share]]), OpenRead, OpenWrite, Create(path), Delete, Move(source,destination), Copy(source,destination[,overwrite]) |
| FileStream | Constructors(path,mode[,access[,share]]), CanRead/CanWrite/CanSeek, Length/Position, Read/Write(byte[],offset,count), Seek, SetLength, Flush, Close/Dispose |
| Directory | Exists, CreateDirectory, Delete(path[,recursive]), Move, Get/SetCurrentDirectory, GetFiles/GetDirectories/GetFileSystemEntries(path), EnumerateFiles/EnumerateDirectories/EnumerateFileSystemEntries(path) |
| DirectoryInfo | Constructor(path), FullName, Name, Exists, Create, Delete([recursive]) |

Paths use `volume:/relative/path`; slash and backslash normalize, leading slash selects the current volume, and dot segments cannot climb above a volume root. Relative paths initially use `sd:/`; SetCurrentDirectory can select another mounted directory. Empty paths, invalid UTF-16, embedded NUL and unknown volume prefixes are rejected. This is a console dialect, not Windows/UNC emulation.

Read/Write/ReadWrite sharing is enforced in a process-local registry, with case-insensitive path comparisons. Delete sharing, async, options/buffering overloads, timestamps, attributes, wildcard/search-option overloads, symlinks, text encodings/readers/writers, FileInfo, MemoryStream, Stream subclasses and DriveInfo are outside this slice. Directory order is unspecified. Recursive deletion refuses volume roots and bounds depth to 64. Streams/cursors require deterministic disposal; unmount refuses active handles. Directory enumeration opens a cursor at GetEnumerator and releases it at EOF, Dispose, early foreach exit or exceptions. No managed threading is available; these tables and card identities assume serial operations.

The backend owns 32 file and 32 directory slots with non-reused handle tokens, 1024-byte path buffers and 255-byte component limits. Descriptor transfers use 4 KiB aligned staging buffers. Offsets are checked against native off_t; managed long does not guarantee arbitrary native file sizes. ReadAllBytes is bounded by the managed heap. Copy uses a 4 KiB managed buffer and removes a new partial destination on failure. Cross-volume file moves copy then delete and are not atomic; directory moves across volumes are unsupported. Ordinary writes and native saves are not power-loss atomic.

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
| DvdDrive | dvd:/, physical DVD interface with read-only ISO9660 |
| MemoryCardSlotA / MemoryCardSlotB | Separate MemoryCard API; no filesystem path |

SD mounts register VFAT/exFAT and select the first mountable partition, with four cache pages of eight sectors (about 16 KiB per device). Mount returns false when no supported medium/filesystem can mount; it never formats media. ISO9660 uses its own pathname limits. Original GameCube disc FST and GC Loader block filesystems are not implemented by DvdDrive. Memory-card enums cannot be passed to Storage.GetPath or ordinary Directory operations.

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

WriteAllBytes stores a logical payload behind a versioned big-endian header; sector padding is omitted by ReadAllBytes. WriteSave adds two 32-byte UTF-8 comment fields (31 text bytes maximum each), an optional 96×32 tiled RGB5A3 Banner (6144 bytes), and an optional 32×32 tiled RGB5A3 Icon (2048 bytes), and sets native CARD status metadata. Images must already use GameCube tiled texture encoding; conversion/animation is not supplied. Title/Comment are storage metadata; no UI is added.

Native CARD reads use aligned 512-byte transfers and writes use aligned card sectors. Each mounted slot needs a 40 KiB native work area, separate from the managed heap. Container construction also temporarily allocates native memory. Existing native-card saves may be overwritten within the same physical allocation; resizing an existing physical allocation currently throws NotSupportedException while preserving its old data. Delete then write is explicit if replacement is acceptable. Card formatting, automatic save repair, resize transactions and power-loss recovery are deferred.

## Verification

CI runs the compiled smoke on 32/64-bit hosts, optimized ASan/UBSan, injected short I/O and failure cleanup, and GC stress, alongside the existing compiler/runtime regressions and GameCube DOL build. Host fixtures test API behavior, not physical hardware. The target build compiles the storage drivers but does not execute mounts or prove FAT encoding, ISO9660 media compatibility, card removal, DMA behavior or save-browser rendering. These remain target acceptance checks.
