using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace DolphinDotNet.SaveImage;

public static class Command
{
    private const string Usage = "Usage: dnd-save-image input.png output.rgb5a3 [--banner] [--fit pad|crop|stretch] [--force]\n" +
        "Default: 32x32 icon, aspect-preserving transparent padding. --banner produces 96x32.\n" +
        "Output is raw 4x4-tiled, big-endian RGB5A3, ready for MemoryCardSaveOptions.Icon/Banner.";

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 1 && (args[0] == "--help" || args[0] == "-h"))
        {
            output.WriteLine(Usage);
            return 0;
        }
        if (args.Length < 2)
        {
            error.WriteLine(Usage);
            return 2;
        }
        bool banner = false, force = false;
        var mode = ResizeMode.Pad;
        for (int i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--banner": banner = true; break;
                case "--force": force = true; break;
                case "--fit" when i + 1 < args.Length:
                    mode = args[++i] switch
                    {
                        "pad" => ResizeMode.Pad,
                        "crop" => ResizeMode.Crop,
                        "stretch" => ResizeMode.Stretch,
                        _ => (ResizeMode)(-1)
                    };
                    if ((int)mode < 0) { error.WriteLine("Invalid --fit value. " + Usage); return 2; }
                    break;
                default: error.WriteLine("Unknown or incomplete option: " + args[i] + "\n" + Usage); return 2;
            }
        }
        string? temporary = null;
        try
        {
            var input = Path.GetFullPath(args[0]);
            var destination = Path.GetFullPath(args[1]);
            if (string.Equals(input, destination, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new ArgumentException("Input and output must be different files.");
            if (!force && File.Exists(destination)) throw new IOException("Output exists; use --force to replace it.");
            using var stream = File.OpenRead(input);
            if (Image.DetectFormat(stream) is not PngFormat) throw new ArgumentException("Input must be a PNG image.");
            stream.Position = 0;
            using var image = Image.Load<Rgba32>(stream);
            if (image.Frames.Count != 1) throw new ArgumentException("Animated PNG is unsupported; use a single frame.");
            int width = banner ? 96 : 32;
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(width, 32), Mode = mode, Position = AnchorPositionMode.Center,
                PadColor = Color.Transparent, Sampler = KnownResamplers.Bicubic
            }));
            var bytes = Rgb5A3.Encode(image);
            // Finish conversion before publishing the destination. Never truncate an existing output on failure.
            temporary = Path.Combine(Path.GetDirectoryName(destination)!, ".dnd-save-" + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, destination, force);
            temporary = null;
            output.WriteLine($"Wrote {width}x32 {(banner ? "banner" : "icon")}: {bytes.Length} bytes to {destination}");
            return 0;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            error.WriteLine("Conversion failed: " + ex.Message);
            return 1;
        }
        finally
        {
            if (temporary != null) File.Delete(temporary);
        }
    }
}
