# Launch another GameCube executable

`Dolphin.IO.DolLauncher` prepares a DOL and requests replacement of the current
application. It supports GameCube AOT builds, in Dolphin and on the console.
Physical-console handoff has not yet been validated.

```csharp
using Dolphin.IO;
using Dolphin.Storage;

static int Main()
{
    if (!Storage.Mount(StorageDevice.SdSerialPort2)) return 1;
    using (var next = DolLauncher.Prepare("sd:/apps/next/boot.dol"))
    {
        DolLauncher.RequestLaunch(next);
    }
    return 0;
}
```

`Prepare(string)` reads through the existing mounted filesystem APIs into native
staging memory, avoiding a managed byte array the size of the executable.
`Prepare(byte[])` snapshots an in-memory image, so the caller may reuse its bytes.
`PrepareAsync(string, CancellationToken)` reads at most 4096 bytes per scheduled
step; cancellation closes the file and releases staging. Filesystems must already
be mounted, including when loading from `dvd:/`.

`PreparedDol.Info` exposes `EntryPoint`, `FileSize`, `SectionCount` and `BssSize`.
Dispose unused preparations explicitly; there is no finalizer. At most eight
preparations may exist. `RequestLaunch` runs on the application thread and queues
one launch, transferring ownership to bootstrap. Disposing that wrapper afterwards
is safe. A second request fails. `SetArguments` can supply standard libogc argv as
described below; no working directory or managed state is transferred.

**Return successfully from Main after requesting launch.** The request does not
terminate Main or interrupt a frame loop. Normal `using`/`finally` cleanup runs,
the generated entry stops asynchronous services, and bootstrap stops audio/input,
closes network sockets and storage handles and unmounts volumes. A nonzero Main
result or pending exception cancels the launch and releases staged preparations.
libogc's shutdown callbacks run before a relocated, stackless PowerPC loader
clears BSS, copies sections, synchronizes caches and branches to the new entry.
The replacement must provide its own CRT/startup, as ordinary standalone DOLs do.

The first implementation accepts cached MEM1 destinations from `0x80003100` to
`0x81800000`, an aligned entry inside a text section, non-overlapping initialized
sections and a file no larger than 24 MiB. BSS is cleared before initialized bytes
are copied, allowing BSS envelopes and padded section tails. Malformed offsets,
wrapped ranges, low-memory/vector writes and unsupported destinations are rejected.
**Staging, loader and descriptors must not intersect any destination or BSS.**
An incompatible layout fails during preparation while the current app is still
running. Large images may also exhaust native memory. ARAM staging, relocation
cycles, Wii/MEM2, encrypted executables and disc/apploader boot are outside this
implementation. Prepared DOLs are executable code and must come from a trusted source.

Validation covers header/range rejection, native ownership and read failures,
managed AOT preparation/cancellation/cleanup with GC stress, and a Dolphin
handoff into an independent libogc executable that verifies data/BSS and creates
a new thread. Build that native fixture with `tests/build_dol_replacement.sh`;
`chain.dol` boots `replacement.dol` from an embedded native snapshot.

## Standard libogc arguments

Before requesting launch, call `executable.SetArguments("swiss-gc.dol",
"Autoload=gcldr:/games/Example.iso", "AutoBoot=Yes")`. Include `argv[0]` explicitly.
Arguments are UTF-8, nonempty and NUL-free, with at most 64 entries and 4096
encoded bytes including separators. Only executables carrying the standard
libogc `_arg` entry marker are accepted; missing markers throw
`NotSupportedException`. Existing launch validation and overlap checks still
apply. This does not implement a relocating loader or bypass incompatible DOL
layouts. The native staging block owns the command line through the handoff;
libogc copies it into its reserved arena before allocating its heap.

`tests/DolArgumentsSmoke` builds a managed caller and a tiny original PPC payload
that reads the actual `_arg` structure after replacement. `verify_native.py`
checks exact UTF-8 bytes, spaces, multiple arguments and terminators through
cloud-local Dolphin’s debugger. This verifies the native handoff, not Swiss
game compatibility or physical-console behavior.
