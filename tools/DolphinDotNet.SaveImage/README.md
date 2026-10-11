# Save-image converter

A desktop .NET 8 command-line tool that converts PNG images into raw GameCube memory-card images. It is an asset preparation tool, not part of the console runtime.

```sh
dotnet run --project tools/DolphinDotNet.SaveImage -- player.png player.rgb5a3
```

The default output is a **32×32 icon, 2,048 bytes**, with aspect-preserving resize and transparent padding. Choose centered cropping or stretching explicitly:

```sh
dotnet run --project tools/DolphinDotNet.SaveImage -- player.png player.rgb5a3 --fit crop
dotnet run --project tools/DolphinDotNet.SaveImage -- banner.png banner.rgb5a3 --banner
```

`--banner` produces **96×32, 6,144 bytes**. `--fit pad|crop|stretch` applies to either size. `--force` allows replacing an existing output. The input is never overwritten; a failed conversion preserves the previous output. `--help` shows usage. Exit codes: 0 success/help, 1 conversion or file failure, 2 invalid arguments.

Output has no header or palette. Pixels use RGB5A3, big-endian words, in GX 4×4 tile order (tiles left-to-right then top-to-bottom; pixels row-major within a tile). Fully opaque pixels use RGB555; other pixels use A3RGB444. Alpha/channel precision is reduced to the target format. Input must be a single-frame PNG; SkiaSharp handles PNG decoding and Mitchell cubic resizing, including transparent padding. No animation is generated.

Load the output from a mounted filesystem:

```csharp
using Dolphin.Storage;
using System.IO;

var options = new MemoryCardSaveOptions
{
    Title = "My Game",
    Comment = "Player save",
    Icon = File.ReadAllBytes("sd:/assets/player.rgb5a3"),
    // Banner = File.ReadAllBytes("sd:/assets/banner.rgb5a3")
};
card.WriteSave("Save01", saveData, options);
```

You can also pack and install it as a local .NET tool:

```sh
dotnet pack tools/DolphinDotNet.SaveImage -c Release -o artifacts/tools
dotnet tool install DolphinDotNet.SaveImage --version 0.1.0 --add-source artifacts/tools --tool-path .tools
.tools/dnd-save-image player.png player.rgb5a3
```

The PNG decoder/resizer uses MIT-licensed [SkiaSharp](https://github.com/mono/SkiaSharp), with packaged native libraries for Windows, macOS and Linux. It requires no build-time license key. Images are limited to 16 megapixels and 64 MiB of encoded data. These dependencies remain confined to desktop tools; managed GameCube applications do not reference them.

## Embed save artwork as C# source

```sh
dotnet run --project tools/DolphinDotNet.SaveImage -- icon.png SaveImages.cs --source --namespace MyGame.Assets --class SaveImages --member Icon
```

Include the generated `.cs` file in the game project, then use:

```csharp
var options = new MemoryCardSaveOptions { Icon = MyGame.Assets.SaveImages.Icon };
card.WriteSave("Save01", saveData, options);
```

The bytes are compiled into the application; no asset file needs to be loaded at runtime. Each property access creates a new byte array, so retain it if reused. `--source` also works with `--banner` and `--disc-banner`. Namespace/class/member defaults are `Assets`, `SaveImages`, `Icon`; select an appropriate member name for banners. Invalid identifiers are rejected and C# keywords are escaped.

## Disc artwork: opening.bnr

```sh
dotnet run --project tools/DolphinDotNet.SaveImage -- disc.png opening.bnr --disc-banner --title "My Game" --maker "My Studio" --description "A GameCube adventure"
```

This creates a complete **BNR1** file (6,496 bytes), including 96×32 tiled RGB5A3 artwork and metadata. Place `opening.bnr` at the root of the GameCube disc filesystem when building the disc image. The current DOL build does not build a disc image or insert this file automatically. Embedding BNR bytes in a DOL alone does not give the disc a banner.

Use `--bnr2` with `--disc-banner` for **PAL BNR2** (8,096 bytes). The supplied metadata is repeated into the six English/German/French/Spanish/Italian/Dutch blocks; separate translations are not currently accepted. Metadata uses Latin-1: short title/maker allow 31 bytes each, long title/maker 63 bytes each, description 127 bytes. `--long-title` and `--long-maker` default to their short equivalents. Unsupported characters, embedded NUL and overlong fields fail without truncation. Japanese Shift-JIS metadata is not supported.

`--banner` remains a raw **memory-card save banner**. `--disc-banner` adds the distinct disc BNR header and metadata. Both support `--fit pad|crop|stretch`. Format layout reference: [Dolphin's VolumeGC.h](https://github.com/dolphin-emu/dolphin/blob/master/Source/Core/DiscIO/VolumeGC.h).
