# ARAM storage and cold assets

`Dolphin.Memory` exposes GameCube auxiliary memory as disposable binary allocations, not pointers or a second managed heap. Objects, strings, transfer tasks, and arrays remain in main RAM. The GameCube backend uses libogc2 ARQ low-priority DMA requests with aligned staging and explicit cache maintenance. Host AOT tests simulate ARAM in native memory.

```csharp
using Dolphin.Memory;

// Initialize on the application thread, before another subsystem claims ARAM.
// Reserve 1 MiB beyond libogc's lower system area for future audio/platform owners.
if (!Aram.Initialize()) throw new InvalidOperationException("ARAM unavailable.");

using var stored = Aram.Allocate(compressedBytes.Length);
await stored.WriteAsync(compressedBytes, 0, 0, compressedBytes.Length);

var chunk = new byte[4096];
await stored.ReadAsync(0, chunk, 0, chunk.Length);
```

Use `Dolphin.Threading.Tasks.Scheduler.Pump()` in the game loop. Each pump performs at most one chunk start/poll transition; a polling turn does not busy-wait for DMA. `Wait`/`GetResult` and `RunUntilIdle` intentionally pump to completion and can block rendering. For frame budgets, limit scheduler pumps per frame. Source arrays must remain unchanged during upload; destination arrays must not be concurrently consumed or modified during download. ARAM transfers are serialized in submission order across all allocations. The queue accepts up to 128 outstanding requests; additional submissions throw until queued work drains. Callbacks only publish a native completion flag; continuations execute on the application thread.


## Standard .NET async compatibility

The long-term public contract is `System.Threading.Tasks.Task`, `Task<T>` and `System.Threading.CancellationToken`; the cooperative scheduler remains an internal implementation detail. The compiler already maps these framework types to `Dolphin.Threading.Tasks` during AOT lowering, but the currently compiled `Dolphin.IO.Stream`, networking and ARAM APIs expose custom task types. Merely replacing a `using` directive does not make the CLR-visible signatures equivalent, and Roslyn does not accept Dolphin's Task as an `async` return type.

Migration needs to proceed across the entire public API and compiler together: (1) inventory/convert task and token signatures, (2) replace internal continuation dependencies with a runtime adapter, (3) teach AOT signature resolution to match framework metadata and converted declarations, (4) compile sample applications against stock .NET contracts, and (5) run native GameCube tests for cancellation and continuation ordering. Avoid mixing framework Task and Dolphin Task in a single public inheritance hierarchy. This work is **not completed** by this PR; the stream currently follows its base `Dolphin.IO.Stream` contract and uses explicit continuations to compile.

## Seekable ARAM streams

`Dolphin.IO.AramStream` supplies a fixed-capacity, disposable `Dolphin.IO.Stream` that owns an `AramBuffer`. It works with `Stream.CopyToAsync`, including HTTP response streams, and copies chunks through the existing 4 KiB staging transfer queue. It does not enable network-to-ARAM direct DMA.

```csharp
using Dolphin.IO;
using Dolphin.Memory;
using Dolphin.Threading.Tasks;

Aram.Initialize(); // only once, before other ARAM owners
using var download = new AramStream(8 * 1024 * 1024);
await httpResponseStream.CopyToAsync(download, 4096);
download.Position = 0;
byte[] header = new byte[256];
int count = await download.ReadAsync(header, 0, header.Length);
// Pump Scheduler.Pump() in the game loop while transfers are pending.
```

The constructor capacity is fixed; logical stream length starts at zero. Appending writes advances the length, and seeking within already-written content permits overwrites. Sparse writes and growing `SetLength` are deliberately unsupported: newly allocated ARAM is uninitialized. `SetLength` can truncate. Writes exceeding capacity fail rather than expanding into main RAM. Only one operation may be pending on a stream at a time. Dispose must occur after any pending transfer has completed. Synchronous `Read` and `Write` pump cooperative tasks and therefore block the calling thread until DMA completes; use async operations in the frame loop. Cancellation may have transferred a partial chunk and does not guarantee transactional rollback.

### Complete GameObject streaming: design boundary

`Model.UnloadToAramAsync()` and `Model.PrepareAsync()` already page shared model packages and native geometry/texture data to and from ARAM. Existing `GameObject` and `ModelInstance` wrappers remain in MEM1, along with their transform and animation playback state; drawing requires `PrepareAsync()` first.

For actual object destruction and reconstruction, define a **versioned object snapshot format** with stable model asset IDs, local transforms, hierarchy/parent identifiers and serialized gameplay-component data, plus animation clip, time, loop and crossfade state. Do not copy C# object memory or native pointers: their addresses, ownership and GC roots cannot be used after reload. A snapshot must be explicitly serialized to bytes, streamed into ARAM, read back, validated and reconstructed against loaded model dependencies; any references to other objects require stable IDs and a second-pass fixup. A future scene streaming layer should expose `UnloadGameObjectAsync` and `RestoreGameObjectAsync` with a registered codec per component type, lifecycle callbacks and atomic rollback. Current `GameObject` supports no generic components or animation-state readback, so full object destruction/restoration is **not** implemented by this stream.

## Ownership and memory bounds

`Aram.Initialize(reservedBytes)` reserves a 32-byte-aligned lower user region, in addition to libogc's system area. It uses internal ARAM only, avoiding expansion/disk regions. Repeated initialization with the same reservation succeeds; changing it is refused. If AR or ARQ already belongs to another subsystem, initialization returns false instead of resetting it or guessing ownership. Integrations must coordinate startup and limit other owners to their reserved region. The service does not discover arbitrary third-party audio allocations or protect against outside raw ARAM access. No shutdown/reset is exposed.

A reservation of 1 MiB is a default budget, not a claim about an audio driver's requirements. Applications can choose another reservation before initialization. Unavailable initialization is explicit; applications may fall back to files or main RAM.

Allocation uses 32-byte-rounded blocks, first-fit holes and implicit coalescing. There are 256 allocation slots, non-reused positive handle tokens and no compaction. `TryAllocate` returns false for space/fragmentation/slot exhaustion; invalid lengths and use before initialization throw. `Statistics` reports usable capacity, rounded allocated bytes, free bytes, largest free block and occupied slot count. Disposed allocations with pending transfers remain included until those transfers drain.

New ARAM allocations have unspecified contents; upload data before reading it. Dispose prevents new submissions immediately. Pending operations retain their allocation and remain valid until completion. Once all holds end, its storage can be reused. Dropping an undisposed buffer leaks its native allocation; deterministic disposal is required.

The service owns roughly 8 KiB of transfer staging/patch memory, plus its fixed allocation table. Payload chunks are at most 4 KiB; aligned DMA spans are at most 4,128 bytes. Uploads use read-modify-write to preserve neighboring bytes for arbitrary offset/count alignment. Native code never keeps managed pointers between calls. The managed work queue retains arrays until completion, keeping them safe across GC. Cancellation skips queued work or subsequent chunks; already submitted DMA drains before resources are released. Cancelled writes/reads may have transferred part of the requested range. Hardware ARQ completion has no per-request media-style error status; DMA faults/hangs need hardware diagnosis.

## Asset cache

```csharp
using var cache = new AramAssetCache(4 * 1024 * 1024);
await cache.StoreAsync("level-2", compressedLevel, priority: 5);
var nextChunk = await cache.PrefetchAsync("level-2", 0, 4096);
```

The cache uses string IDs and a rounded-byte budget. It evicts lower-priority recoverable entries first, then least recently used entries among equal priorities. `evictable: false` keeps an entry until explicit removal/disposal. Compression, persistence and reload policy belong to the application; the cache does not silently compress assets or load their files.

`ReadAsync` copies a range into a caller buffer. `PrefetchAsync` allocates a main-RAM buffer and fetches a requested range for imminent consumption; it does not automatically predict future asset usage. Hold on to the returned buffer only while needed.

Stores are serialized per cache: await one before starting another or removing entries. A new/ replacement entry is published only after upload completes. A replacement preserves the old entry on cancellation/failure, but may evict other recoverable entries to stage its data. Its temporary allocation must also fit the cache budget and native free space. Outstanding reads safely finish even if their entry is evicted. Disposing a cache frees entries immediately or after their queued transfers drain; a pending store faults rather than publishing into a disposed cache.

## Verification

Portable native tests cover fragmentation/coalescing, slot exhaustion, stale handles, alignment boundaries, adjacent-byte preservation, pending disposal and injected transfer failure. Compiled managed tests exercise multi-chunk round trips, submission order, cancellation, lifecycle, priority eviction, atomic cache replacement and prefetch under optimized sanitizers/GC stress and 32-bit execution. GameCube CI compiles both the backend and generated managed ABI. Actual console DMA/cache behavior, frame-time measurements and coexistence with an audio subsystem remain hardware acceptance checks.

Backend API references: [libogc2 ARAM](https://github.com/extremscorner/libogc2/blob/master/include/ogc/aram.h) and [ARQ](https://github.com/extremscorner/libogc2/blob/master/include/ogc/arqueue.h).
