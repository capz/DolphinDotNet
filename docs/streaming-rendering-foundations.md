# Hypercube-oriented streaming/rendering foundation

**Experimental feature branch**: `feature/hypercube-render-streaming-foundations`

This is a DolphinDotNet platform/API expansion, **not** a complete sparse virtual-texturing engine. It intentionally keeps page tables, world cells, mip selection, view prediction and residency/eviction in Hypercube.

## API surface

| API | Behavior | Important limit |
| --- | --- | --- |
| `GraphicsDevice.InsertFence`, `GpuFence.IsCompleted` | Conservative present-frame completion marker | Frame-end `GX_DrawDone`, not sub-frame GPU token or GPU timestamp |
| `GraphicsDevice.RetireAfter` | Hold managed disposables until a fence completes | Queue limit 512; disposed on frame exit/explicit collection |
| `Texture2D.CreateNative`, `ReplaceNative` | Allocate and replace GX-tiled I4/I8/IA4/IA8/RGB565/RGB5A3/RGBA8/CMPR data | Complete mip-0 payload only; synchronous, outside active render passes |
| `StreamingTexture` | Owned reusable native texture and content generation | `UploadNative` is synchronous; texture must be loaded before material draw |
| `AramTransferQueue` | Aligned native MEM1↔ARAM DMA, 64 KiB per subtransfer, serialized by ARQ busy retry | 32-byte-aligned offsets/length, caller must avoid concurrent buffer access |
| `AssetStream` | Async, bounded chunked random access to archive files, writes into `NativeBuffer` on application executor | One in-flight operation per stream, current 8 KiB staging, no speculative I/O merge |
| `RenderQueue` | Groups opaque, depth-writing submissions by material instance | Submission reordering only; does **not** merge geometry or automatically eliminate GX state changes |
| `GpuProfiler.Snapshot` | Counts GX backend draw submissions, attempted texture binding operations, triangles | Software submission counts, **not** physical GPU busy time, cache misses or texture DMA bandwidth |

## Example

```csharp
using Dolphin.Graphics;
using Dolphin.Diagnostics;
using Dolphin.Memory;
using Dolphin.Storage;

// Between frames:
using var page=new StreamingTexture(136,136,NativeTextureFormat.Cmpr);
page.UploadNative(compiledGxCmprPage); // exactly page.NativeBytes bytes
var material=new Material { Texture=page.Texture };

// Optionally prefetch asset bytes into reusable native memory:
using var staging=NativeBuffer.Allocate(64*1024);
using var stream=new AssetStream("sd:/world.gcpak");
int read=await stream.ReadAtAsync(8192,staging,0,64*1024);

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
- Direct ARAM DMA pins the native MEM1 buffer and retains its ARAM allocation until completion; a managed `Dispose` while transferring cannot reuse its storage.
- Ordinary ARAM byte-array transfers and new native-buffer DMA contend for the same hardware request slot and retry on busy. No concurrent ARQ requests are sent through this code path.
- `AssetStream.ReadAtAsync` stages reads in 8 KiB managed chunks on the background executor, publishes each chunk to native memory from the application executor and honors cancellation between chunks. On completion, inspect returned byte count for EOF.
- GX-native texture updates invalidate the GPU texture cache after CPU flush; full replace waits for GPU completion first. Future page-table implementations should batch publication and avoid dozens of per-page waits.

## Remaining prerequisites for actual megatextures

1. Offline CMPR page packer with gutters/mipmap hierarchy and texture-space patch compilation.
2. Multi-page transaction batching and batched native uploads without redundant GX waits.
3. True multi-request, priority-aware storage scheduler and ARAM cache residency policy.
4. Precompiled geometry/display lists and low-overhead material/texture state changes.
5. Console profiling (GC Loader/original DVD/SD2SP2), GPU cache metrics and audio coexistence stress tests.

## Validation

The new APIs must compile through both the AOT intrinsic mappings and native GameCube backend. Hardware runtime behavior and throughput are **not established** by source-only review. See the existing `tests/GraphicsSmoke`, `tests/AramSmoke`, and `samples/TextureStrategy` work for validation patterns.
