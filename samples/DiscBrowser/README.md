# DiscBrowser — GC Loader and GameCube ISO filesystems

This sample shows the three GameCube Storage workflows. The GC Loader SD storage is
a separate `StorageDevice.GcLoader` mount at `gcloader:/`, backed by libogc2
`__io_gcode`. It is available only when compatible GC Loader hardware/firmware
exposes the corresponding DVD block-device protocol. Failure to mount returns
`false` instead of pretending the SD card is a normal DVD image.

For an ISO stored on a mounted FAT/exFAT volume, open a **GameCube** (not
ISO9660) disc image via `GameCubeImage.Open("gcloader:/games/demo.iso")` or
`GameCubeImage.Open("sd:/games/demo.iso")`. The image API checks the native
GameCube magic, FST bounds, names and file extents before exposing read-only
directory listings, `FileExists`, `DirectoryExists`, `ReadAllBytes`, and
`OpenRead` (seekable `Dolphin.IO.Stream`). `GetFiles` and `GetDirectories`
return *immediate child names*, not full paths.

For a physical game disc or a loaded image presented by an optical-drive
emulator, `Storage.MountGameCubeDisc()` uses the existing validated FST driver
on `dvd:/`. This is also equivalent to
`Storage.MountDisc(DiscFormat.GameCubeFst)`. `Storage.MountDisc(DiscFormat.Iso9660)`
remains for ISO9660-format data discs, not original GameCube game discs.

These operations are read-only as regards GameCube FST content.
Do not assume GC Loader block storage and DVD reads can be used concurrently;
both may share drive hardware. Close image file streams before disposing the
image, and close both images and file streams before unmounting their host volume.

**Limitations:** only plain original GameCube ISO/GCM (not RVZ, WBFS, NKit,
CISO or Wii partition filesystems); the image must fit the platform's
32-bit seekable stdio (typically below 2 GiB). `ReadAllBytes` requires the
file to fit the managed heap, so prefer `OpenRead` for large assets.
The GC Loader interface requires hardware acceptance tests; Dolphin cannot
simulate the GC Loader SD-card block protocol. Treat writable GC Loader
capability as firmware-dependent. Results are not a substitute for testing
real-drive DOL startup, drive reset, and card behavior.

Build the DOL from the repository root:

```sh
dotnet build samples/DiscBrowser/DiscBrowser.csproj -c Release
dotnet run --project tools/DolphinDotNet.Compiler -- --bundle samples/DiscBrowser/bin/Release/net8.0/DiscBrowser.dll out/DiscBrowser --platform gamecube
make -C out/DiscBrowser
```

Insert a GameCube disc or set up an optical drive emulator; to try the image
path, place `games/demo.iso` on the GC Loader card. The sample safely skips
unavailable mounts.
