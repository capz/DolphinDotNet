# Hypercube-oriented streaming/rendering foundation

**Experimental feature branch**: `feature/hypercube-render-streaming-foundations`

This is a DolphinDotNet platform/API expansion, **not** a complete sparse virtual-texturing engine. It intentionally keeps page tables, world cells, mip selection, view prediction and residency/eviction in Hypercube.

## API surface

| API | Behavior | Important limit |
| --- | --- | --- |
| `GraphicsDevice.InsertFence`, `GpuFence.IsCompleted` | Conservative present-frame completion marker | Frame-end `GX_DrawDone`, not sub-frame GPU token or GPU timestamp |
| `GraphicsDevice.RetireAfter` | Hold managed disposables until a fence completes | Queue limit 512; disposed on frame exit/explicit collection |
| `Texture2D.CreateNative`, `ReplaceNative` | Allocate and replace GX-tiled I4/I8/IA4/IA8/RGB565/RGB5A3/RGBA8/CMPR data | Complete mip-0 payload only; synchronous, outside active render passes |
| `StreamingTexture.StageUploadAsync` / `TryPublish` | Background CPU staging to inactive GX allocation; application-thread swap with cache invalidation | Double MEM1 allocation; no GX_DrawDone on publish; rebind materials to new `Texture` |
| `StreamingTexture.UploadNative` | Legacy synchronous replacement | Between frames only; prefer detached staging |
| `AramTransferQueue` | Aligned native MEM1↔ARAM DMA, 64 KiB per subtransfer, serialized by ARQ busy retry | 32-byte-aligned offsets/length, caller must avoid concurrent buffer access |
| `AssetStream` | Async, bounded chunked random access to archive files, writes into `NativeBuffer` on application executor | One in-flight operation per stream, current 8 KiB staging, no speculative I/O merge |
| `RenderQueue` | Coalesces same-material **and same-transform** opaque meshes into bounded native vertex submissions | Up to 128 meshes per native call, split at 768 vertices; distinct transforms are not flattened |
| `GpuProfiler.Snapshot` | Counts GX backend draws and texture bindings actually issued by the cached GX material path, plus triangles | Software counters, **not** physical GPU busy time, cache misses or texture DMA bandwidth |

## Example

```csharp
using Dolphin.Graphics;
using Dolphin.Diagnostics;
using Dolphin.Memory;
using Dolphin.Storage;

// Construction between frames; stage asynchronously while the game renders.
using var page=new StreamingTexture(136,136,NativeTextureFormat.Cmpr);
var material=new Material();
await page.StageUploadAsync(compiledGxCmprPage); // exactly page.NativeBytes bytes
page.TryPublish(material); // binds newly published texture

// Optionally prefetch asset bytes into reusable native memory:
using var staging=NativeBuffer.Allocate(64*1024);
using var stream=new AssetStream("sd:/world.gcpak");
int read=await stream.ReadAtAsync(8192,staging,0,64*1024);
// Publish one GX-native page from a native staging region, with no byte[] copy:
// await page.StageUploadAsync(staging,0); // source contains 136x136 CMPR bytes
// page.TryPublish(material);

// Direct ARAM promotion (after ARAM initialized and both addresses aligned):
using var aram=Aram.Allocate(64*1024);
await AramTransferQueue.CopyToAramAsync(staging,0,aram,0,64*1024);
await AramTransferQueue.CopyToMainAsync(aram,0,staging,0,64*1024);

// Scene draws, scheduled on the application executor:
device.BeginFrame(Color32.Black);
renderer.Begin3D(camera);
queue.Submit(mesh,material,transform);
var grouped=queue.Execute(renderer);
renderer.End3D();
var fence=device.InsertFence();
device.RetireAfter(fence,page); // only if page will not be submitted again
device.EndFrame();
var counters=GpuProfiler.Snapshot();
```

Do not retire a texture still referenced by a material submitted on a subsequent frame. The above snippet demonstrates independent APIs; real games will use a bounded resource manager to coordinate them.

## Correctness rules

- GPU submissions are synchronous from the application executor; native DMA progresses through the background queue. Do not issue raw GX in DMA callbacks.
- A frame fence is signaled **only** after GX has finished the corresponding frame. Marking a resource for retirement does not imply it can be used subsequently.
- AOT native-call lowering normally enforces application-executor access for every graphics and texture operation. Only `TextureStageArray` and `TextureStageBuffer` bypass that check: these detached operations use CPU memory copies and data-cache flushing, and cannot issue GPU commands. `TexturePublishStaged`, allocation, draws, and cache invalidation remain application-executor-only.
- `StageUploadAsync` writes only the inactive GX allocation; no GPU wait occurs. After `TryPublish`, the previously active allocation is not reused until the current GPU frame finishes. Rebind each material to `StreamingTexture.Texture` after publication; existing materials retain the previous `Texture2D` object.
- Publishing issues a GPU texture-cache invalidate but is not a GPU-fence wait; do not publish while raw GX primitives are active.
- `RenderQueue` merges only identical material and transform *object* references. Non-identical transforms and transparency still submit separately. GX material-state caching is invalidated at pass changes and raw/2D transitions.
- Direct ARAM DMA pins the native MEM1 buffer and retains its ARAM allocation until completion; a managed `Dispose` while transferring cannot reuse its storage.
- Ordinary ARAM byte-array transfers and new native-buffer DMA contend for the same hardware request slot and retry on busy. No concurrent ARQ requests are sent through this code path.
- `AssetStream.ReadAtAsync` stages reads in 8 KiB managed chunks on the background executor, publishes each chunk to native memory from the application executor and honors cancellation between chunks. On completion, inspect returned byte count for EOF.
- GX-native texture updates invalidate the GPU texture cache after CPU flush; full replace waits for GPU completion first. Future page-table implementations should batch publication and avoid dozens of per-page waits.

## Remaining prerequisites for actual megatextures

1. Offline CMPR page packer with gutters/mipmap hierarchy and texture-space patch compilation.
2. Multi-page transaction batching, texture cache invalidate coalescing, and reduced double-buffer memory pressure.
3. True multi-request, priority-aware storage scheduler and ARAM cache residency policy.
4. Precompiled geometry/display lists and low-overhead material/texture state changes.
5. Console profiling (GC Loader/original DVD/SD2SP2), GPU cache metrics and audio coexistence stress tests.

## Validation

The new APIs must compile through both the AOT intrinsic mappings and native GameCube backend. Hardware runtime behavior and throughput are **not established** by source-only review. See the existing `tests/GraphicsSmoke`, `tests/AramSmoke`, and `samples/TextureStrategy` work for validation patterns.
