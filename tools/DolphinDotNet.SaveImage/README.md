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

Output has no header or palette. Pixels use RGB5A3, big-endian words, in GX 4×4 tile order (tiles left-to-right then top-to-bottom; pixels row-major within a tile). Fully opaque pixels use RGB555; other pixels use A3RGB444. Alpha/channel precision is reduced to the target format. Input must be a single-frame PNG; ImageSharp handles PNG decoding and bicubic resizing, including transparent padding. No animation is generated.

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

The PNG decoder/resizer dependency is SixLabors.ImageSharp 3.1.12, licensed under the [Six Labors Split License](https://github.com/SixLabors/ImageSharp/blob/v3.1.12/LICENSE). Its dependency remains confined to the desktop converter; managed GameCube applications do not reference it.
