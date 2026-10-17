# DiscBrowser — unified GameCube disc and ISO browsing

Both the inserted/current ODE GameCube game and a plain GameCube ISO file
are presented as the same read-only `dvd:/` filesystem. The only difference
is the mount argument:

```csharp
Storage.MountDisc();              // Physical game or ODE-selected game
Storage.MountDisc("sd:/games/game.iso"); // ISO file on a mounted SD volume
```

After mounting, use the usual `Storage.GetFiles(StorageDevice.DvdDrive, "")`,
`Storage.ReadAllBytes(StorageDevice.DvdDrive, "opening.bnr")`,
`Storage.FileExists`, `Storage.GetDirectories`, or
`Storage.Open(StorageDevice.DvdDrive, ...)`. All are read-only.

For a GC Loader card, first call
`Storage.Mount(StorageDevice.GcLoader)` and then mount a full path such as
`Storage.MountDisc("gcloader:/games/demo.iso")`. This second mount requires
firmware exposing the libogc2 `__io_gcode` block interface; **GC Loader SD
storage and ODE DVD access might not operate concurrently**.

Unmount in reverse order: close all `dvd:/` file streams, unmount
`StorageDevice.DvdDrive`, then unmount the backing `StorageDevice.GcLoader`
or other SD volume. Source unmount while the image is mounted is rejected.
ISO/GCM only, not RVZ/CISO/Wii; the file must fit native 32-bit stdio seeking.

Build:

Open [DiscBrowser.sln](DiscBrowser.sln) in Visual Studio or Rider and use the Dolphin
launch profile, or build and run from the repository root:

```sh
dotnet run --project samples/DiscBrowser -c Release
```

See [IDE setup](../../docs/ide-workflow.md) for prerequisites and local settings.

A DOL-only Dolphin launch may have no disc inserted. The sample then shows
`NO GAMECUBE DISC OR DEMO ISO AVAILABLE` instead of waiting for a disc-cover
close event. To browse a real game image, set Dolphin's **Default ISO** before
launching the DOL (a plain GameCube ISO/GCM). The current disc is tried before GC Loader, whose probe resets the drive.
GC Loader browsing additionally
requires its firmware-backed SD interface; a PC file path is not a mounted
GameCube filesystem path.

The sample lists at most eight root files using a lazy enumerator, closes the
enumerator, and unmounts volumes even if metadata or directory reads fail.
Errors remain visible on the result screen. Z toggles statistics; Start exits.

CI exercises ISO mounting, file access, read-only errors, and cleanup through the
actual GameCube devoptab/newlib interfaces in headless Dolphin, using a synthetic
read-only backing device. It also starts DiscBrowser without media. GC Loader
and physical drive behavior still require hardware testing.
