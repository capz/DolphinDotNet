# SDK-owned GameCube apploader

GameCube native builds now produce a complete `apploader.img` in addition to the
DOL and ELF. `dotnet build -p:DndBuildNative=true` copies it beside the executable
as `<AssemblyName>.apploader.img`; `dndc --bundle` plus `make` produces the same
component inside the native directory. `make apploader` uses the same build path.
Wii/Wii U and managed-only/design-time builds do not gain a GameCube boot step.

The SDK owns the freestanding PowerPC loader, linker layout, toolchain flags,
cache maintenance, zero-initialized loader BSS and deterministic 0x20-byte IPL
header. No Nintendo apploader bytes are used. The original AnimalTown loader is
MIT licensed; its attribution/license remains in `boot/gamecube/LICENSE` and in
packaged native sources. The normal SDK package license is unchanged.

Place the **entire image**, including its header, at disc offset **0x2440**. This
loader expects the main DOL at **0x10000**. Preserve the disc header, BI2, FST,
opening banner and assets in your disc packer. A DOL plus an apploader image alone
is not a complete bootable ISO. Do not prepend another IPL header.

The image packer validates PPC32 big-endian ELF sections, executable entry,
allocated memory span, overlap, padding and the space before the DOL. It copies
allocated NOBITS sections as zeros so initialization never depends on IPL RAM
contents. `make apploader` and the standalone `DOL=` option additionally validate DOL DMA alignment, payload and
entry ranges, section overlap, BSS and reserved boot memory. Ordinary `make` keeps standalone DOL builds independent of that optional disc check. Standalone DOLs can
use more MEM1; this disc loader requires application sections/BSS to stay below
`0x81200000`. Invalid input does not replace an existing output image.

## Independent boot-component consumption

The loader has no managed runtime dependency. Consumers pinned to an older SDK
ABI can use a separately pinned SDK apploader without upgrading their compiler,
managed libraries or native rendering/storage adapters. The GameCube SDK
apploader workflow cross-compiles this component with the SDK's pinned libogc2
image and publishes immutable `apploader-<commit12>` GitHub prereleases containing
`apploader.img`, ELF, MIT license, `BUILD.json` and `SHA256SUMS.txt`. A consumer
must pin the full SDK commit and image SHA-256 and verify them before disc packing.
Do not resolve `latest` or treat an unverified cached image as trusted.

For development or rebuilding a pinned image from source:

```sh
make -f boot/gamecube/Makefile OUTPUT="$PWD/out/apploader/apploader"
# Validate the application's DOL as well when available:
make -f boot/gamecube/Makefile OUTPUT="$PWD/out/apploader/apploader" DOL=/path/to/Game.dol
```

Only devkitPPC, make and Python are needed for this component; it does not link
the managed runtime or require third-party game assets. NuGet consumers receive
the same source/build files under `tools/compiler/native/boot/gamecube/`.

Host tests exercise image layout, invalid ELF/DOLs, failure atomicity, the IPL
callback sequence, section reads, cache-handoff ordering and application BSS
clearing. Cross-compilation and emulator boot are separate validation steps;
physical IPL/DVD/GameCube qualification must be reported independently.
