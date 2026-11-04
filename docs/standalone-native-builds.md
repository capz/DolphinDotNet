# Standalone AOT build bundles

Run from the DolphinDotNet repository checkout:

```sh
dotnet run --project tools/DolphinDotNet.Compiler -- --bundle bin/Release/net8.0/MyGame.dll out/MyGame
cd out/MyGame
make
```

The **default is GameCube**; `--platform gamecube` produces exactly the same Makefile and output. The other accepted values are `--platform wii` and `--platform wiiu`.

The bundle contains generated managed C, copied native runtime sources and headers,
`scripts/dol_sections.py`, a devkitPPC Makefile, and `DEPENDENCIES.txt`.

**GameCube bundles also contain the verified EXI and VIDEO source corrections and build
scripts**. The first `make` clones the pinned libogc2 upstream source
(requires `git` and network access), verifies both corrections,
and builds a local `build/exi-overlay/libogc.a` used *before* the SDK library.
This fixes the EXI polling register and a 576p blanking overflow that can hang
resolution/scan changes in `VIDEO_WaitVSync`. The archive replaces both `exi.o`
and `video.o`; the installed toolchain is unchanged. The historical script and
archive names are retained for compatibility. Subsequent builds reuse the overlay
and rebuild it when either correction changes.

This applies to every generated GameCube app, including raw `VIDEO_*` use, and
the repository Makefile. The NuGet SDK includes the corrected sources and verifier.
Existing apps must update the SDK and rebuild their native DOL to receive the fix;
already-built executables and older packages are not changed.

At 576p, the original black timing placed 1218 half-lines in a 10-bit pre-blanking
field, truncating it to 194. Sharing the blank interval between the pre/post fields
keeps both within range and preserves field length. See the native regression in
`tests/display_switch_gamecube.c`: it holds output black before the first frame,
then exercises 480p/576p and 576i/576p changes. Build and run it with:

```sh
bash scripts/build_exi_overlay.sh
bash tests/build_display_switch.sh out/display-switch
python3 scripts/dolphin_smoke.py out/display-switch/display-switch.dol \
  --elf out/display-switch/display-switch.elf --gdb-port 2169 --seconds 60 --component-cable \
  --expect-word dnd_video_test_result=1 --expect-word dnd_video_test_phase=7 \
  --expect-word dnd_video_test_frames=16 --memory-after-symbol dnd_video_test_complete \
  --logs out/display-switch/dolphin
```

The host CI also compiles the actual vendored vertical-register function and
checks blank/unblank timing and register bounds for standard and cropped modes.
Emulator acceptance does not establish physical console/cable/display behavior.

## Platform contract

| Platform | Toolchain | Rules | Native output | Status |
|---|---|---|---|---|
| GameCube (default) | devkitPPC + libogc2 GameCube | `libogc2/gamecube_rules` | `.dol` | Uses repository-verified GameCube runtime |
| Wii | devkitPPC + libogc2 Wii | `libogc2/wii_rules` | `.dol` | Experimental: shared native code requires hardware-specific auditing |
| Wii U | devkitPPC + wut | `wut/share/wut_rules` | `.rpx` | Experimental: only minimal managed runtime; native feature adapters are not implemented |

The Wii U output never includes GameCube-specific native files. Wii U requests needing networking, storage, ARAM, graphics, or controller backends fail at generation rather than silently linking GameCube code. Wii builds use Wii headers and library directories but do not yet guarantee Wii-native compatibility for all shared backends.

Do **not** assume a bundle compiling implies hardware compatibility. Test against Dolphin (GameCube/Wii) and physical hardware. Wii U requires its own wut-native platform/graphics/input/network/storage implementations before general feature support can be claimed.

## Namespace-to-library map

The compiler walks reachable AOT methods rather than `using` directives. For each feature below it records the match and native dependency. The existing GameCube runtime links its full verified baseline because its native code is monolithic; removing a library just because a namespace was not directly referenced could create unresolved dependencies.

| Feature | Declaring type namespace | GameCube / Wii native libraries |
|---|---|---|
| TCP, UDP, DNS, HTTP | `System.Net.*`, `Dolphin.Network.*` | `-lbba -logc` |
| Storage, streams, files | `System.IO.*`, `Dolphin.Storage.*` | `-lfat -ldvm -liso9660 -logc` |
| ARAM | `Dolphin.IO.Aram*`, `Dolphin.Aram.*` | `-logc` |
| Graphics and models | `Dolphin.Graphics.*`, `Dolphin.Models.*` | `-lopengx -logc -lm` |
| Controller input | `Dolphin.Input.*` | `-logc` |
| PCM WAV sound | `Dolphin.Audio.*` | `-laesnd -logc` (libogc2 GameCube/Wii) |
| Other sound APIs | `System.Media.*`, `Dolphin.Sound.*` | Unsupported; generator rejects them |

Wii U uses **wut** libraries, not any of the above GameCube/Wii link flags. The mapping is a starting point: namespaces alone cannot prove native dependency closure, particularly for reflection, implicit runtime calls and transitive C references.

The generated Makefile automatically runs `elf2dol` and the existing 32-byte DOL section validator for GameCube/Wii; Wii U instead emits an RPX through `elf2rpl`. Each platform must use its matching SDK and toolchain.

## API compatibility hierarchy

Managed feature/API availability is cumulative:

| Target | Allowed managed API families |
|---|---|
| GameCube | GameCube |
| Wii | GameCube + Wii |
| Wii U | GameCube + Wii + Wii U |

This is an **API surface compatibility rule**, not permission to reuse incompatible native binaries. The Wii U's PowerPC CPU does not make Wii/libogc2 object files, video/graphics initialization, OS services, or networking automatically compatible with wut/RPX. Every shared managed API must map to a native backend implemented for the target console. The bundle records these intended managed API families in its manifest, and platform-exclusive reachable API namespaces are now rejected for lower platform targets; native adapters for Wii and Wii U still need to be implemented before treating those targets as fully supported.


The current namespace enforcement reserves `Dolphin.Wii.*` and `DolphinDotNet.Wii.*` for Wii-and-newer APIs, and `Dolphin.WiiU.*` and `DolphinDotNet.WiiU.*` for Wii U-only APIs. Other existing shared `Dolphin.*` APIs are considered part of the GameCube-compatible managed surface. This is intentionally checked against **reachable compiled method declarations**, not source `using` statements.
