# Standalone AOT build bundles

Run from the DolphinDotNet repository checkout:

```sh
dotnet run --project tools/DolphinDotNet.Compiler -- --bundle bin/Release/net8.0/MyGame.dll out/MyGame
cd out/MyGame
make
```

The **default is GameCube**; `--platform gamecube` produces exactly the same Makefile and output. The other accepted values are `--platform wii` and `--platform wiiu`.

The bundle contains generated managed C, copied native runtime sources and headers, `scripts/dol_sections.py`, a devkitPPC Makefile, and `DEPENDENCIES.txt`.

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
| Sound | `System.Media.*`, `Dolphin.Audio.*`, `Dolphin.Sound.*` | No verified backend; generator rejects it |

Wii U uses **wut** libraries, not any of the above GameCube/Wii link flags. The mapping is a starting point: namespaces alone cannot prove native dependency closure, particularly for reflection, implicit runtime calls and transitive C references.

The generated Makefile automatically runs `elf2dol` and the existing 32-byte DOL section validator for GameCube/Wii; Wii U instead emits an RPX through `elf2rpl`. Each platform must use its matching SDK and toolchain.
