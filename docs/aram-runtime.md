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

The runtime services queued DMA automatically; use ordinary await without scheduler pump calls. Source arrays must remain unchanged during upload, and destination arrays must not be concurrently consumed or modified during download. Transfers are serialized in submission order, with up to 128 queued requests. DMA callbacks publish only a native completion flag. Default awaits resume on their captured executor. See [automatic async/await](async-runtime.md).


## Standard .NET async compatibility

The public I/O, ARAM and networking APIs use `System.Threading.Tasks.Task`, `Task<T>` and `System.Threading.CancellationToken`. `Dolphin.IO.AramStream` derives from `System.IO.Stream`, so standard HTTP response streams can copy into it with `CopyToAsync`. Application methods should declare `async Task` using the standard namespace; no scheduler import is needed for normal awaits.

The AOT compiler redirects supported standard stream/task/awaiter/cancellation metadata to the cooperative Dolphin runtime. This supplies compatibility on the target, not a desktop GameCube backend or the full .NET API surface. See [async runtime and namespace rules](async-runtime.md) for supported signatures and limitations.

## Seekable ARAM streams

`Dolphin.IO.AramStream` supplies a fixed-capacity, disposable `System.IO.Stream` that owns an `AramBuffer`. It works with `Stream.CopyToAsync`, including HTTP response streams, and copies chunks through the existing 4 KiB staging transfer queue. It does not enable network-to-ARAM direct DMA.

```csharp
using Dolphin.IO;
using Dolphin.Memory;
using System.Threading.Tasks;

Aram.Initialize(); // only once, before other ARAM owners
using var download = new AramStream(8 * 1024 * 1024);
await httpResponseStream.CopyToAsync(download, 4096);
download.Position = 0;
byte[] header = new byte[256];
int count = await download.ReadAsync(header, 0, header.Length);
// Await each operation; the runtime advances DMA and continuations automatically.
```

The constructor capacity is fixed; logical stream length starts at zero. Appending writes advances the length, and seeking within already-written content permits overwrites. Sparse writes and growing `SetLength` are deliberately unsupported: newly allocated ARAM is uninitialized. `SetLength` can truncate. Writes exceeding capacity fail rather than expanding into main RAM. Only one operation may be pending on a stream at a time. Dispose must occur after any pending transfer has completed. Synchronous `Read` and `Write` wait through the runtime dispatcher and block the calling thread until DMA completes; use async operations in the frame loop. Cancellation may have transferred a partial chunk and does not guarantee transactional rollback.

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

## Example: download Google's PNG logo into ARAM

See [GoogleLogoAram](../samples/GoogleLogoAram/Program.cs). The sample downloads an image as an HTTP response, copies it with standard `Stream.CopyToAsync` using one reusable 4 KiB buffer in main RAM (MEM1), awaiting each write to `aramStream`, a fixed-capacity 256 KiB allocation in GameCube auxiliary RAM (ARAM). ARAM is extra memory outside the CPU's directly accessible main RAM. Streaming this way avoids holding the complete downloaded image in MEM1, leaving more main RAM for gameplay and other tasks. It does not bypass main RAM entirely: network reception and ARAM DMA still use small staging buffers. The sample uses standard `System.Threading.Tasks.Task` and `System.Net.Http.HttpClient` APIs. When the developer is ready to work with the data, they can set `aramStream.Position` to the desired offset and call `ReadAsync` to copy just the required bytes back to MEM1 for decoding, texture upload, or other processing. The sample demonstrates this by reading back and validating the PNG signature; it does **not** decode or display the image. Keep the `AramStream` alive until its contents are needed, then dispose it to release the ARAM allocation. The sample disposes it at the end of the demonstration.

### Async Main and automatic progress

The sample uses ordinary `async Task<int> Main()`. It awaits the download, validates the PNG signature, returns 0 on success or 1 on failure, and stops networking in finally. The generated native bootstrap owns automatic network service, DMA dispatch and continuation execution. No application pump calls or busy-wait loop are needed.

The GameCube network stack currently supports plain HTTP but **not HTTPS**. Google's public logo image is served via HTTPS, so this example uses a minimal local development relay that fetches the logo from Google using TLS on a PC, then serves the unchanged PNG to the GameCube over HTTP. It is not an HTTPS implementation or a secure transport from the relay to the console.

1. On a trusted computer on the same LAN, run `python3 samples/GoogleLogoAram/logo_proxy.py` (requires internet access).
2. Change `LogoUrl` in `samples/GoogleLogoAram/Program.cs` from `192.168.1.2` to that computer's IPv4 address. Allow local TCP port 18081 through the firewall.
3. Build the example with `dotnet build samples/GoogleLogoAram/GoogleLogoAram.csproj -c Release` and AOT-compile the resulting assembly with DolphinDotNet.Compiler, as with other managed GameCube examples. Native backend execution requires devkitPPC/libogc2 and a correctly initialized network interface.
4. Run the GameCube application with the LAN connected. The runtime services the download automatically; successful transfer returns exit code 0, while a failed download, insufficient ARAM or an invalid PNG returns exit code 1.

The sample is an integration demonstration, not a full-featured downloader. Its size cap protects ARAM and it does not keep the stream after returning; an actual game should retain the `AramStream` as an asset owner until needed for rendering or decoding. If the logo URL changes or redirects, update the relay's upstream URL. Do not expose the relay to untrusted networks; it uses plain HTTP without authentication. Rendering a PNG requires a decoder and GX texture upload, neither of which happens in this sample.
