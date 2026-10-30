using System;
using System.IO;
using Dolphin.Formats;
using DolphinDotNet.Imaging;

namespace DolphinDotNet.Assets;

/// <summary>Read-only GCFT-inspired inspection plus explicit RARC repacking tools.</summary>
internal static class GameCubeAssetCommands
{
    public static void Run(string[] args)
    {
        if (args.Length < 3) throw new ArgumentException(
            "gcft inspect INPUT | gcft extract ARCHIVE OUTPUT_DIR | gcft pack INPUT_DIR OUTPUT.arc | gcft bti INPUT.bti OUTPUT.png | gcft bfn INPUT.bfn OUTPUT_DIR | gcft bmg INPUT.bmg");
        switch (args[1])
        {
            case "inspect":
                if (args.Length != 3) throw new ArgumentException("gcft inspect INPUT");
                Inspect(File.ReadAllBytes(args[2]));
                break;
            case "extract":
                if (args.Length != 4) throw new ArgumentException("gcft extract ARCHIVE OUTPUT_DIR");
                Extract(args[2], args[3]);
                break;
            case "pack":
                if (args.Length != 4) throw new ArgumentException("gcft pack INPUT_DIR OUTPUT.arc");
                File.WriteAllBytes(args[3], RarcPacker.PackFolder(args[2]));
                break;
            case "bti":
                if (args.Length != 4) throw new ArgumentException("gcft bti INPUT.bti OUTPUT.png");
                WriteBtiPng(File.ReadAllBytes(args[2]), args[3]);
                break;
            case "bfn":
                if (args.Length != 4) throw new ArgumentException("gcft bfn INPUT.bfn OUTPUT_DIR");
                ExtractBfn(File.ReadAllBytes(args[2]), args[3]);
                break;
            case "bmg":
                if (args.Length != 3) throw new ArgumentException("gcft bmg INPUT.bmg");
                DumpGenericBmg(File.ReadAllBytes(args[2]));
                break;
            default: throw new ArgumentException("Unknown gcft subcommand.");
        }
    }

    private static void Inspect(byte[] source)
    {
        var data = GameCubeBinary.UnwrapYaz0(source);
        string four = GameCubeBinary.Ascii(data, 0, 4);
        if (four == "RARC")
        {
            var archive = RarcArchive.Read(source);
            Console.WriteLine("RARC (" + archive.Entries.Count + " files)");
            foreach (var e in archive.Entries) Console.WriteLine(e.Length + "\t" + e.Path);
        }
        else if (four == "MESG")
        {
            var bmg = BmgMessages.Read(source);
            Console.WriteLine("BMG (" + bmg.Messages.Count + " messages)");
            foreach (var m in bmg.Messages) Console.WriteLine(m.Id + "\t" + m.EscapedText);
        }
        else if (four == "JPAC")
        {
            var jpc = JpcArchive.Read(source);
            Console.WriteLine(jpc.Version + " (" + jpc.Particles.Count + " particles, " + jpc.Textures.Count + " textures)");
            foreach (var p in jpc.Particles) Console.WriteLine("Particle " + p.Id.ToString("X4") +
                ": " + string.Join(", ", System.Linq.Enumerable.Select(p.Chunks, c => c.Type)));
        }
        else if (four == "FONT")
        {
            var glyphs = BfnGlyphSheets.Read(source);
            Console.WriteLine("BFN (" + glyphs.Sheets.Count + " glyph sheets)");
        }
        else if (four == "J3D1")
        {
            var animation = J3dMaterialAnimation.Read(source);
            Console.WriteLine(animation.FileType + " (" + animation.TrackCount +
                " tracks, " + animation.DurationFrames + " frames)");
        }
        else
        {
            var bti = BtiTexture.Read(source);
            Console.WriteLine("BTI " + bti.Width + "x" + bti.Height +
                " format " + bti.ImageFormat + ", " + bti.MipCount + " mip levels");
        }
    }

    private static void Extract(string archivePath, string destination)
    {
        var archive = RarcArchive.Read(File.ReadAllBytes(archivePath));
        string root = Path.GetFullPath(destination);
        string prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries)
        {
            string full = Path.GetFullPath(Path.Combine(root, entry.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new InvalidDataException("Archive member escapes output directory.");
            string? parent = Path.GetDirectoryName(full);
            if (parent == null) throw new InvalidDataException("Invalid archive output path.");
            Directory.CreateDirectory(parent);
            File.WriteAllBytes(full, archive.ReadFile(entry));
        }
        Console.WriteLine("Extracted " + archive.Entries.Count + " files.");
    }

    private static void DumpGenericBmg(byte[] data)
    {
        var catalog = BmgCatalog.Read(data);
        Console.WriteLine("BMG encoding " + catalog.EncodingCode + " (" + catalog.Entries.Count + " messages)");
        foreach (var entry in catalog.Entries)
            Console.WriteLine(entry.Id.ToString("X8") + "\t" + entry.EscapedText);
    }

    private static void ExtractBfn(byte[] data, string outputDirectory)
    {
        var font = BfnGlyphSheets.Read(data);
        Directory.CreateDirectory(outputDirectory);
        for (int index = 0; index < font.Sheets.Count; index++)
        {
            var sheet = font.Sheets[index];
            WriteRgbaPng(sheet.Width, sheet.Height, sheet.Rgba,
                Path.Combine(outputDirectory, "sheet-" + index.ToString("D3") + ".png"));
        }
        Console.WriteLine("Exported " + font.Sheets.Count + " BFN glyph sheets.");
    }

    private static void WriteBtiPng(byte[] data, string output)
    {
        var bti = BtiTexture.Read(data);
        WriteRgbaPng(bti.Width, bti.Height, bti.Rgba, output);
    }

    private static void WriteRgbaPng(int width, int height, byte[] rgba, string output)
    {
        using var image = new RasterImage(width, height);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int i = (y * width + x) * 4;
            image[x, y] = new Rgba32(rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]);
        }
        image.SaveAsPng(output);
    }
}
