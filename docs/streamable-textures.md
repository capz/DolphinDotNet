# Independently streamable texture levels

DNLT v1 (`.dnlod`) stores independently checksummed GX-native images. Level 0 is
the finest; physical payload order is coarsest first. A header/index read is
sufficient to discover levels; image payloads are validated separately, after
bounded reads. This format does not change DNTX v1/v2 or `StreamingTexture`.

```sh
dnd-assets texture grass.png grass.dnlod --streamable --mipmaps --format cmpr
dnd-assets texture grass.png grass.dnlod --streamable --mipmaps --format rgba8 --min-size 8
dnd-assets texture grass.png grass.dnlod --streamable --mipmaps --levels 0,2,4 --gutter 4
dnd-assets texture --low grass_low.png --high grass_high.png grass.dnlod --streamable --quality-pair
```

`--format auto` (default) uses CMPR for opaque/binary-alpha images and RGBA8 for
smooth alpha. Explicit CMPR rejects smooth alpha rather than making it opaque.
Mips use premultiplied-aware box filtering in byte/sRGB space. Binary CMPR mip
coverage retains the nearest representable number of opaque texels, with stable
raster-order ties. CMPR encoding is deterministic RGB565 endpoint quantization
and nearest-palette matching; it is not a perceptual/high-effort compressor.

`--min-size` is a power-of-two cutoff for the largest dimension; short axes
clamp at 1. `--levels` preserves the original mip exponents and must lie above
the cutoff. A low/high pair has exactly HIGH (0) and LOW (1), accepts independent
non-power-of-two dimensions and never synthesizes intermediate images. Without
`--mipmaps`, a standalone streamable image has one entry and flags 0.

`--gutter` extrudes 0–16 border pixels around each independently stored image.
The index includes per-level source-normalized scale/offset mappings. These
identify a finite positive usable rectangle within the native image; source
mip dimensions (native dimensions times UV scale) must follow the authored
half-size progression. Compose these mappings with authored/scrolled material
UVs instead of modifying mesh geometry. Guttered native dimensions must still
fit the 1024-texel limit. Native padding replicates edge texels.

The tool also writes `OUTPUT.manifest.json`: deterministic level dimensions,
byte ranges, CRC32, UV mappings, format/alpha decisions and source RGBA hashes.
CLI manifests additionally include the encoded source-file SHA-256 hashes.

## Binary contract

All metadata is little endian. The 64-byte header is: magic `DNLT`, version 1,
flags (1 mip chain, 2 sparse mips, 4 quality pair), logical full width/height,
default GX format, entry count, initial coarse entry index, table offset (64),
first data offset, file length, metadata CRC32, optional UV-table offset and
length, then eight zero-reserved bytes. Flags 0 describe one standalone image;
valid mip flag combinations are 1 and 3; quality pair is 4.

Each 32-byte table entry is logical LOD (u32), native width/height (u16 each),
GX format (u32: 6 RGBA8 or 14 CMPR), zero flags, offset, padded native byte count,
payload CRC32 and zero reserved. Table order is increasing logical LOD. The
optional UV table follows immediately: float32 scaleU, scaleV, offsetU, offsetV
for each entry. Metadata CRC32 covers both tables, excluding the header/padding.
An absent UV table means identity. Payloads are contiguous, 32-byte aligned and
ordered from coarsest to finest; total file length is bounded to 16 MiB.
RGBA8 uses 4×4, 64-byte tiles; CMPR uses 8×8, 32-byte tiles, including tiny mips.

`Dolphin.Formats.StreamableTextureDescriptor.GetMetadataBytes(header, fileLength)`
validates a 64-byte header before a bounded metadata read. `Read(prefix,
fileLength)` validates metadata without touching payloads and returns an
immutable descriptor. `GetLevel` returns independent native ranges, logical
exponents and UV mappings. Validate each payload using its CRC, or incrementally
with `ComputeCrc32(..., initial)`, before GPU publication. Descriptor parsing
never allocates GPU resources and never implicitly reads a complete file.

Run `dotnet run --project tests/StreamableTextureTests -c Release` for independent
BTI pixel decoding, coverage/filtering, sparse/pair/gutter cases, corrupt-header
and truncation checks, fuzz inputs and DNTX compatibility checks.

## Detached runtime uploads

`DetachedTextureUpload.Create(width, height, format, nativeBytes, crc32)` owns
one private allocation, charged through `StagedBytes`. Between frames, read at
most your configured chunk budget into a reusable aligned `NativeBuffer`, then
await `StageChunkAsync(source, sourceOffset, destinationOffset, count)`. Chunks
must be sequential, contiguous and 32-byte aligned. Both allocations stay held
until the service copy and application continuation finish. Do not mutate raw
addresses while held; normal buffer writes and storage reads reject reuse.

`CompletedBytes` observes the completed task and incremental native-source CRC.
`Seal()` rejects missing bytes or a checksum mismatch. `TryPublish` invalidates
the GX texture cache on the application executor and transfers one immutable
`Texture2D` to the caller. No copy or global GPU wait occurs at publication.
The owner no longer counts the allocation after transfer; the caller counts
`Texture2D.NativeBytes` as active, and later as retired.

On cancellation, await `AbortAsync()` before reusing either allocation.
`Dispose()` refuses a running copy; it cancels queued work but never frees
memory under a worker. Failed uploads cannot publish. No whole-image managed
staging array or permanent second texture is required. Existing
`StreamingTexture` keeps its fixed-size front/back contract.

Compose authored animation with each level's static usable rectangle using
`authored.Then(levelMapping)`. `UvTransform.Then` preserves a complete affine
matrix, including nonuniform gutters after rotation; no geometry rebuild or
lossy decomposition into scale/rotation is necessary.

Record the final reader using `GraphicsDevice.InsertFence()`. Once the texture
is removed from all draw bindings, `Texture2D.TryDisposeAfterFence(fence)` frees
it only when that fence completes, without another GPU wait. A fence cannot
protect later submissions: resolve and pin immediately before drawing, and
never submit a retired texture. Normal `Dispose()` retains its conservative
existing behavior.

Run `python3 scripts/ci_detached_textures.py` for native range/lifetime tests
and Debug/Release managed AOT with GC stress. Build
`tests/fixtures/DetachedTextures` and run
`python3 scripts/dolphin_detached_textures.py Game.dol --logs out/fresh-capture`
for repeated RGBA8/CMPR resolution changes and independent pixel-patch checks.
The fixture uses two explicitly counted 32-KiB source buffers, with at most one
detached destination per lane. Emulator timings are not console measurements.
