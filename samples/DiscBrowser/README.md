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

GC Loader and physical drive behavior require hardware testing.
