using DolphinDotNet.Imaging;

namespace DolphinDotNet.SaveImage;

public static class Command
{
    private const string Usage = "Usage: dnd-save-image input.png output.rgb5a3 [--banner] [--fit pad|crop|stretch] [--force]\n" +
        "Default: 32x32 icon, aspect-preserving transparent padding. --banner produces 96x32.\n" +
        "--source emits C# bytes (--namespace Assets --class SaveImages --member Icon).\n" +
        "--disc-banner emits opening.bnr (--bnr2 for PAL); metadata: --title, --maker, --long-title, --long-maker, --description.";

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
        bool banner = false, force = false, source = false, disc = false, bnr2 = false;
        string ns = "Assets", className = "SaveImages", member = "Icon";
        string title = "", maker = "", longTitle = "", longMaker = "", description = "";
        var mode = ResizeMode.Pad;
        for (int i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--banner": banner = true; break;
                case "--force": force = true; break;
                case "--source": source = true; break;
                case "--disc-banner": disc = true; break;
                case "--bnr2": bnr2 = true; break;
                case "--namespace" when i + 1 < args.Length: ns = args[++i]; break;
                case "--class" when i + 1 < args.Length: className = args[++i]; break;
                case "--member" when i + 1 < args.Length: member = args[++i]; break;
                case "--title" when i + 1 < args.Length: title = args[++i]; break;
                case "--maker" when i + 1 < args.Length: maker = args[++i]; break;
                case "--long-title" when i + 1 < args.Length: longTitle = args[++i]; break;
                case "--long-maker" when i + 1 < args.Length: longMaker = args[++i]; break;
                case "--description" when i + 1 < args.Length: description = args[++i]; break;
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
        if ((!disc && (bnr2 || title.Length != 0 || maker.Length != 0 || longTitle.Length != 0 || longMaker.Length != 0 || description.Length != 0)) || (disc && banner))
        {
            error.WriteLine("Disc metadata/--bnr2 require --disc-banner; --banner and --disc-banner are distinct modes.");
            return 2;
        }
        string? temporary = null;
        try
        {
            var input = Path.GetFullPath(args[0]);
            var destination = Path.GetFullPath(args[1]);
            if (string.Equals(input, destination, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new ArgumentException("Input and output must be different files.");
            if (!force && File.Exists(destination)) throw new IOException("Output exists; use --force to replace it.");
            using var image = RasterImage.Load(File.ReadAllBytes(input), pngOnly: true);
            int width = banner || disc ? 96 : 32;
            using var resized = image.Resized(width, 32, mode);
            var bytes = Rgb5A3.Encode(resized);
            if (disc) bytes = DiscBanner.Encode(bytes, bnr2, title, maker, longTitle, longMaker, description);
            if (source) bytes = System.Text.Encoding.UTF8.GetBytes(SourceOutput.Generate(bytes, ns, className, member));
            // Finish conversion before publishing the destination. Never truncate an existing output on failure.
            temporary = Path.Combine(Path.GetDirectoryName(destination)!, ".dnd-save-" + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, destination, force);
            temporary = null;
            output.WriteLine($"Wrote {width}x32 {(disc ? "disc banner" : banner ? "save banner" : "icon")}: {bytes.Length} bytes to {destination}");
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
