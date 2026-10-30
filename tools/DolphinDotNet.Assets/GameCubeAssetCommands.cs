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
            "gcft inspect INPUT | gcft extract ARCHIVE OUTPUT_DIR | gcft pack INPUT_DIR OUTPUT.arc | gcft bti INPUT.bti OUTPUT.png");
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

    private static void WriteBtiPng(byte[] data, string output)
    {
        var bti = BtiTexture.Read(data);
        using var image = new RasterImage(bti.Width, bti.Height);
        for (int y = 0; y < bti.Height; y++)
        for (int x = 0; x < bti.Width; x++)
        {
            int i = (y * bti.Width + x) * 4;
            image[x, y] = new Rgba32(bti.Rgba[i], bti.Rgba[i + 1], bti.Rgba[i + 2], bti.Rgba[i + 3]);
        }
        image.SaveAsPng(output);
    }
}
