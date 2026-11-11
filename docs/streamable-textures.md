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
