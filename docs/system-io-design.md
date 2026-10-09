# System.IO File and Directory design

Status: proposed; no new filesystem implementation is included in this document.
Source audit: 2026-10-09. Baseline public contract: .NET Standard 2.0, matching the managed Core project. Additional modern .NET overloads require separate coverage.

## Design decision

Implement ordinary `System.IO` APIs as managed algorithms over a small native filesystem backend. Use libogc2-compatible libdvm to mount FAT/exFAT volumes and expose devoptab operations; use the devkitPPC C runtime for file descriptors and directory iteration. Keep hardware discovery and mounting under `Dolphin.Storage`, and keep game/application file code under `System.IO`.

The current project still selects legacy libogc in `Makefile` and CI. It has no filesystem mount initialization or filesystem library in `LIBS`. A DOL loading successfully from Swiss does not establish that the application has mounted the same medium.

## Verified upstream building blocks

Source snapshots inspected:

- [libogc2 da237745](https://github.com/extremscorner/libogc2/tree/da237745c9de889ea55d2ca8a3902d081d1324fc).
- [libdvm 420b9ae7](https://github.com/extremscorner/libdvm/tree/420b9ae7beff18c361cbbfbe7671cabdf3dd26a5).
- [devkitPro directory example](https://github.com/devkitPro/gamecube-examples/blob/master/filesystem/directory/source/directory.c).
- [Newlib C library documentation](https://sourceware.org/newlib/libc.html).

| Layer | Available surface | Purpose and constraints |
| --- | --- | --- |
| libogc2 SD device drivers | `get_io_gcsda`, `get_io_gcsdb`, `get_io_gcsd2`; `DISC_INTERFACE` | Block access for supported SD adapters. These are not pathname APIs. Header: `include/sdcard/gcsd.h`. |
| libdvm volume management | `dvmInit`, `dvmInitDefault`, `dvmRegisterFsDriver`, `dvmProbeMountDiscIface`, `dvmMountVolume`, `dvmUnmountVolume`, `dvmDeinit` | Device/partition probing, filesystem registration and volume lifetime. Header: `include/dvm.h`. |
| libdvm FAT compatibility | `fatInitDefault`, `fatInit`, `fatMountSimple`, `fatMount`, `fatUnmount` | Convenience/compatibility layer. Current `fatInit` registers both VFAT and exFAT; `fatMountSimple` explicitly mounts VFAT. Do not assume the latter selects exFAT automatically. Header/source: `include/fat.h`, `source/fat_wrappers.c`. |
| C descriptor APIs | `open`, `close`, `read`, `write`, `lseek`, `fstat`, `fsync`, `ftruncate` | Small unbuffered backend for `FileStream`; libdvm's FAT devoptab implements the corresponding hooks. Check the installed toolchain's `off_t` width. |
| C stdio | `fopen`, `fclose`, `fread`, `fwrite`, `fseek`, `ftell`, `fflush` | Suitable convenience alternative; avoid mixing buffered stdio and descriptor I/O on one handle. `fflush` alone is not durable media synchronization. |
| Paths/directories | `stat`, `mkdir`, `rmdir`, `unlink`, `rename`, `chdir`, `getcwd`; `opendir`, `readdir`, `closedir` | libdvm supplies stat, directory, rename and removal hooks; the devkitPro example demonstrates `DIR` iteration. Parent creation and recursive traversal are managed algorithms. |
| FAT attributes | `FAT_getAttr`, `FAT_setAttr`; `ATTR_READONLY`, `ATTR_HIDDEN`, `ATTR_SYSTEM`, `ATTR_DIRECTORY`, `ATTR_ARCHIVE` | Device-specific attribute support; POSIX permissions are synthesized by the FAT driver. |
| Volume information | `statvfs` / devoptab `statvfs_r` | Capacity/free-space support for a later `DriveInfo` slice. |
| Optional ISO9660 | `ISO9660_Mount`, `ISO9660_Unmount`, `ISO9660_GetVolumeLabel` | Separate optical filesystem backend; current header advertises a 128-byte maximum path constant. Treat as an optional read-only capability, not a writable FAT volume. |
| GameCube memory cards | `CARD_Init`, `CARD_Mount`, `CARD_Open`, `CARD_Create`, `CARD_Read`, `CARD_Write`, `CARD_Close`, `CARD_Delete`, directory/status functions | A separate save-file system with game/company identity and block/alignment rules. It is not an SD/FAT hierarchy. Do not represent it as unrestricted `System.IO.Directory` support. |

The current libdvm GameCube initialization probes `carda`, `cardb`, `sd` (Serial Port 2), `dvd` (GC Loader), and `ram` (Memory Expansion Pak), subject to actual hardware and filesystem availability. Here `carda`/`cardb` are block-device adapters, not ordinary Nintendo memory-card saves. Additional partitions can receive generated names; enumerate successfully mounted volumes rather than hard-coding one name.

The inspected defaults are 32 cache pages of eight 512-byte sectors: about 128 KiB of data cache per cached device, plus metadata (hardware block alignment can increase this). Configure an explicit native cache budget instead of mounting every device with unexamined defaults.

libdvm's GameCube build specifies 32-byte I/O buffer alignment. Its cache/device layer handles disk access; our backend should use bounded aligned staging buffers so arbitrary managed array offsets never become an undocumented DMA assumption.

## Layering

1. Managed `File`, `Directory`, `Path`, `FileInfo`, `DirectoryInfo` and `FileStream` implement argument validation, recursion, copying, buffering and object lifetime.
2. An internal managed filesystem bridge calls native operations through registered, signature-checked bindings. An initial explicit compiler intrinsic slice is acceptable, but source-level recognition must remain centralized.
3. Native portable backend owns descriptors, directory cursors, errno snapshots, short-I/O handling and buffer conversion. Its host implementation is tested against temporary directories.
4. GameCube backend owns mounting, hardware interfaces, filesystem capability reporting, aligned buffers and shutdown. Managed applications see paths and stream objects, not libogc structs.

Do not add a native implementation for every high-level .NET overload. `ReadAllBytes`, copy, recursive deletion and search filtering should reuse the primitive bridge.

## Public API rollout

| Stage | Managed APIs | Native mapping / dependencies |
| --- | --- | --- |
| 1: path and stream foundation | `Path.Combine`, `GetFullPath`, `GetFileName`, `GetDirectoryName`, `GetExtension`, `IsPathRooted`; `FileMode`, `FileAccess`, `FileShare`, `SeekOrigin`; basic disposable `FileStream` | Path grammar, normalized volume identity, descriptor registry, read/write/seek/length/flush/truncate/close |
| 2: binary files | `File.Exists`, `Open`, `OpenRead`, `OpenWrite`, `Create`, `ReadAllBytes`, `WriteAllBytes`, `Copy`, `Move`, `Delete` | `stat`, descriptor operations, exclusive creation, rename/removal; managed bounded copying |
| 3: directory operations | `Directory.Exists`, `CreateDirectory`, `Delete(path)`, `Delete(path, recursive)`, `Move`, `GetCurrentDirectory`, `SetCurrentDirectory`, `GetFiles`, `GetDirectories`, `GetFileSystemEntries` | `mkdir`, `rmdir`, `rename`, directory cursors; minimal `DirectoryInfo` because `CreateDirectory` returns it |
| 4: lazy traversal | `EnumerateFiles`, `EnumerateDirectories`, `EnumerateFileSystemEntries`; pattern and `SearchOption` overloads | Custom disposable generic enumerator; existing enumerable contract work must cover early termination and exceptions |
| 5: text and metadata | UTF-8 `ReadAllText`, `WriteAllText`, `AppendAllText`, line helpers; `StreamReader`, `StreamWriter`; `FileInfo`, fuller `DirectoryInfo`, attributes and timestamps | Managed `Encoding`/text buffering and metadata semantics; not unlocked merely by binary I/O |
| Later | Async APIs, richer `FileOptions`, `DriveInfo`, additional encoding and filesystem backends | Requires explicit scheduler, cancellation, capability and API coverage decisions |

For the initial binary stages, reject unsupported share/options combinations explicitly. Do not silently accept async, encryption, permissions/ACLs, watcher, link or replacement semantics that the backend cannot provide. `FileSystemWatcher`, memory-mapped files and OS access-control APIs have no implementation in this design.

## Path model

- Preserve devoptab volume prefixes, for example `sd:/games/demo/save.bin` or `carda:/games/demo/save.bin`. These examples require those actual mounted names.
- Relative paths resolve against a runtime-owned current directory initialized from a verified mounted application location, with a configured fallback. No storage mount is a valid startup state; queries can return false and operations report an error.
- Leading `/` means the root of the current volume. Fully qualified `name:/...` paths identify a particular volume. Reject ambiguous `name:relative` forms initially.
- Normalize separator spelling, `.` and `..` lexically, retaining the volume root. Never let normalization switch volumes or climb above a volume root. Specify this console path dialect in tests; it is not Windows drive-letter or UNC emulation.
- Convert managed UTF-16 to the backend's selected filename encoding at the bridge. Reject embedded NUL and invalid surrogate sequences. Verify UTF-8 filename behavior against the pinned FatFs configuration before claiming Unicode path compatibility.
- Keep paths in their original case. Expose case sensitivity as a volume property and verify FAT lookup/pattern matching behavior; do not lowercase UTF-16 paths as an implementation shortcut.
- Use checked lengths and the backend's actual path/name limits. Do not inherit ISO9660's limits for FAT or assume a desktop `MAX_PATH`.
- Runtime current directory is process-wide and protected alongside the handle table. Pass absolute normalized paths to native operations, avoiding hidden dependence on C `chdir` during each operation.

## Open and lifetime semantics

| FileMode | Descriptor flags | Managed behavior |
| --- | --- | --- |
| `CreateNew` | access flags + `O_CREAT | O_EXCL` | Fail if the destination exists; no check-then-open race |
| `Create` | access flags + `O_CREAT | O_TRUNC` | Create or truncate |
| `Open` | access flags | Fail if absent |
| `OpenOrCreate` | access flags + `O_CREAT` | Preserve existing contents |
| `Truncate` | access flags + `O_TRUNC` | Must fail if absent; verify driver behavior and compensate in the backend if necessary |
| `Append` | `O_WRONLY | O_CREAT | O_APPEND` | Write only; additionally enforce .NET append-origin seek restrictions |

Use opaque integer handles with generation checks; never store a native `FILE*`/`DIR*` as a managed GC reference. `Dispose` is idempotent, and reads/writes on a closed stream throw `ObjectDisposedException`. Deterministic disposal is mandatory; a finalizer is not the initial cleanup mechanism.

Maintain a process-local sharing registry over normalized volume/path identities. Enforce both the existing handle's share permissions against the new access and the new share permissions against existing access. This cannot enforce ownership against an external actor modifying the medium. Begin with explicitly tested sharing combinations, and document FAT case/alias limitations.

Stream offsets and length are managed `long`. Translate to native `off_t` with checked conversions and prove the installed ABI width with a compile probe. If it is narrower, reject out-of-range operations; never truncate. exFAT support does not prove arbitrary stream sizes are representable by the C ABI.

Handle short reads, EOF, partial writes and interrupted calls. Validate array offset/count before entering C. A native call borrowing a managed buffer must not allocate managed objects or trigger collection; keep the owning array rooted throughout it. Use an aligned staging buffer when needed. Release native resources before raising a managed exception, since exception lowering uses nonlocal jumps.

## File and directory behavior

- `File.Exists` and `Directory.Exists` distinguish regular files and directories with `stat`; they return false for null/invalid/inaccessible paths without leaving a pending managed exception. They do not prove a later open will succeed.
- `ReadAllBytes` checks the supported array/heap limit, handles empty files, changed lengths and short reads, and closes on every exit. Prefer a managed bounded-read algorithm over returning an untracked `malloc` buffer.
- `WriteAllBytes` checks write and close/flush errors. Ordinary writes do not promise power-loss atomicity. A separate save helper may use a temporary file and synchronization, with filesystem-specific durability claims.
- `File.Delete` succeeds when the file is absent but does not recursively delete a directory. `Copy(overwrite: false)` uses exclusive destination creation; copy errors preserve the original error while cleaning up the new partial destination where appropriate.
- Same-volume moves use rename, with destination-exists behavior verified against the selected driver. Cross-volume file moves can use copy-then-delete, reporting that the operation is not atomic. Cross-volume directory moves are rejected.
- `CreateDirectory` creates parents and succeeds for an existing directory; a file occupying any component is an error. Return a real `DirectoryInfo` instance.
- Nonrecursive directory deletion fails for a nonempty directory. Recursive traversal skips `.`/`..`, uses bounded iterative state, and never assumes directory ordering. It refuses volume-root deletion.
- `Get*` methods materialize arrays; `Enumerate*` methods hold native cursors only while an enumerator is active. `foreach` early exit and exceptions must close cursors. Deferred-open/error timing is tested against the selected .NET contract.
- Search patterns are managed algorithms. Start with documented `*`/`?` matching and add reference-runtime tests before claiming all .NET wildcard edge cases. Traversal depth and memory use are bounded; exceeding a bound produces an explicit error.
- FAT metadata is limited: inspected `fstat` synthesizes permissions and returns zero file times. Timestamp methods must use verified pathname metadata rather than presenting those zeros as genuine creation/access times. Unsupported timestamp/attribute operations fail explicitly.

## Errors

Native bridge operations return a result plus a captured error code and operation context. They do not use the current global `DndSystemException` channel to communicate a completed operation. Cleanup errors must not erase an earlier read/write error.

| Condition | Managed result |
| --- | --- |
| Null argument / invalid argument / invalid offset or count | `ArgumentNullException`, `ArgumentException`, `ArgumentOutOfRangeException` before native access |
| `ENOENT` on a file operation | `FileNotFoundException` if the final file is missing; `DirectoryNotFoundException` for a missing parent, using operation/context checks |
| Missing/unmounted volume, `ENODEV` | `DriveNotFoundException` or the documented operation-specific IO error |
| `EACCES`, `EPERM`, `EROFS` | `UnauthorizedAccessException` with read-only context |
| `ENAMETOOLONG` | `PathTooLongException` |
| `EEXIST`, `ENOSPC`, `EBUSY`, generic media `EIO` | `IOException` with preserved error details |
| Native allocation failure | `OutOfMemoryException` |
| Unsupported backend capability | `PlatformNotSupportedException` / `NotSupportedException`, according to API contract |
| Exists query errors | `false`; no pending exception |

libdvm's FAT errno mapping collapses both missing file and missing path into `ENOENT`; missing media can become `EIO`, and denied operations can become `EACCES`. Do not promise distinctions that the driver does not preserve. Capture errno immediately, before close/stat/message-building changes it.

## Build and mounting changes

1. Add a pinned libogc2 CI/toolchain configuration using its documented `gamecube_rules` and compatible OpenGX library. Keep a legacy-libogc build only if intentionally supported.
2. Install/pin compatible libdvm (`libogc2-libdvm` package family) and link its FAT facade and dependencies from the selected package. Current upstream builds `libfat` over libdvm; verify the exact linker line with a compile/link probe rather than mixing old libfat and new libdvm archives.
3. Initialize filesystem support before managed entry. Prefer explicit runtime working-directory selection; `fatInitDefault` can change CWD based on loader/environment state.
4. Record successful mounted volumes/capabilities and cache/native memory use separately from the managed heap. No device is required for graphics-only execution.
5. Close streams/cursors and synchronize writable media before orderly unmount/reset. Handle removal during an operation as an error; unmount must not invalidate a live handle silently.

Optical assets and native memory-card save support are follow-up backends. Native memory-card saves should initially use a dedicated `Dolphin.Storage.MemoryCard` API with explicit save identity and block rules.

## Existing implementation gaps

`source/system.c` has native `Exists`, `ReadAllBytes` and `WriteAllBytes` helpers, but they are not mapped as normal `System.IO` calls by `IntrinsicRegistry`. `Exists` opens the file instead of checking its kind; reads allocate outside the managed heap and collapse open failures to not-found; several seek/close failures are not reported; writes ignore close errors. `Path.Combine` also does not implement device-prefix semantics. Replace these helpers behind the new bridge rather than treating them as completed .NET APIs.

## Verification and delivery order

1. Backend compile probes: descriptor/directory APIs, `off_t`, FAT/exFAT mount, path encoding, case lookup, rename/destination behavior, and directory EOF/error conventions.
2. Portable native tests with temporary directories and injected short I/O, full media, read-only, removal and cleanup failures.
3. Compiled C# differential tests for argument validation, every open mode, empty/large files, seek/truncate/append, sharing conflicts, directory creation/deletion, copying/moving and enumeration disposal.
4. GC-stress and ASan/UBSan execution on 32/64-bit hosts, including allocations while directory results are being constructed and exceptions during traversal.
5. GameCube build with pinned libraries and a small storage smoke DOL. Test SD adapter mounting, round-trip files/directories and read-only/missing/removal cases on Dolphin where emulated and actual hardware where needed.
6. Promote each exact supported overload in the .NET compatibility index only after compiled execution evidence is recorded. A successful DOL link alone is not storage acceptance.

## Contract references

- [.NET Standard overview](https://learn.microsoft.com/en-us/dotnet/standard/net-standard).
- [File.Exists contract](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.exists?view=netstandard-2.0).
- [FileMode](https://learn.microsoft.com/en-us/dotnet/api/system.io.filemode).
- [libogc2 migration guidance](https://github.com/extremscorner/libogc2#readme).
