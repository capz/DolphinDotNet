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

```sh
dotnet build samples/DiscBrowser/DiscBrowser.csproj -c Release
dotnet run --project tools/DolphinDotNet.Compiler -- --bundle samples/DiscBrowser/bin/Release/net8.0/DiscBrowser.dll out/DiscBrowser --platform gamecube
make -C out/DiscBrowser
```

GC Loader and physical drive behavior require hardware testing.
