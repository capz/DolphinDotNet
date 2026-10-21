# DiscBrowser — GC Loader SD and GameCube disc browsing

DiscBrowser defaults to browsing the **GC Loader SD card**, rather than
automatically opening the disc that GC Loader currently presents through the
DVD interface. With compatible firmware, its root is available as `gcloader:/`
through `Storage.Mount(StorageDevice.GcLoader)`. The browser enumerates files
and directories on that card, not just one hard-coded `games/demo.iso`.

Use the D-pad or left analog stick to select an entry (350 ms initial repeat,
then every 100 ms). **A** opens a directory. On the GC Loader card, A on a
plain **ISO/GCM** file mounts its GameCube FST read-only at `dvd:/` and opens
that image's directory listing. **B** goes to the parent directory; B at an
opened image's root returns to the original GC Loader SD directory.

**Y** switches the source between GC Loader SD and the physical/ODE
DVD interface, unmounting the previous source first. **X** refreshes/remounts
the current source, **Start** exits, and **Z** toggles diagnostics. DVD is
never auto-selected when an SD mount fails: the screen instead reports that
GC Loader SD is unavailable (check firmware), and Y explicitly opens the DVD.
An unavailable source does not prevent controller input or status rendering.

Both an original disc or an ODE-presented disc and a plain ISO file are
presented as the same read-only `dvd:/` filesystem:

```csharp
Storage.MountDisc();                        // Physical or ODE-presented game
Storage.MountDisc("gcloader:/games/game.iso"); // ISO from a mounted GC Loader SD
```

For an ISO mount, keep the backing card mounted until the DVD FST is unmounted:
close open streams, unmount `StorageDevice.DvdDrive`, and then unmount the
backing `StorageDevice.GcLoader`. Firmware may not allow SD-sector access at
the same time as ODE DVD access. ISO/GCM only, not RVZ/CISO/Wii; image files
must fit native 32-bit stdio seeking. Non-image files can be highlighted but
are not executed by this sample.

Build:

Open [DiscBrowser.sln](DiscBrowser.sln) in Visual Studio or Rider and use the
Dolphin launch profile, or build and run from the repository root:

```sh
dotnet run --project samples/DiscBrowser -c Release
```

See [IDE setup](../../docs/ide-workflow.md) for prerequisites and local settings.
For Dolphin, set **Default ISO** to an ordinary GameCube ISO/GCM if you want
to browse a disc, then press **Y** after launch to switch to the DVD source.
Dolphin may not expose the real GC Loader SD block interface; in this case
the default screen reports SD unavailable rather than silently opening DVD. Hardware testing is needed
to verify GC Loader firmware SD-sector access.

Entries are sorted with directories first and are bounded to 256 folders plus
256 files per directory. Eight rows are visible at once. Disc titles and banners
are best-effort; unreadable metadata does not block folder navigation. Mounts
and banner textures are cleaned up on refresh, source changes and exit.

CI checks plain-disc FST access through the native devoptab in headless Dolphin,
as well as pipe-controller scrolling, A/B folder navigation, refresh and exit.
A separate test-only GC Loader volume exercises invalid-image recovery, opening an
ISO, returning to its card folder, opening a different GCM, and switching between
the card and the presented DVD using real controller input. Only card discovery
and file contents are synthetic; the managed browser and native ISO/FST mounts
are the production implementations. The host regression tests additionally cover SD/DVD path normalization and
ISO/GCM file recognition. GC Loader SD and ODE concurrency need testing on
physical hardware.
