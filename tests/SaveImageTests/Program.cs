using DolphinDotNet.SaveImage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
static ushort Word(byte[] bytes, int pixel) => (ushort)((bytes[pixel * 2] << 8) | bytes[pixel * 2 + 1]);
static int Run(params string[] args) => Command.Run(args, new StringWriter(), new StringWriter());

using (var fixture = new Image<Rgba32>(32, 32))
{
    fixture[0, 0] = new Rgba32(255, 0, 0, 255);
    fixture[1, 0] = new Rgba32(0, 255, 0, 255);
    fixture[0, 1] = new Rgba32(0, 0, 255, 255);
    fixture[4, 0] = new Rgba32(255, 255, 255, 255);
    fixture[0, 4] = new Rgba32(0, 0, 0, 255);
    fixture[2, 0] = new Rgba32(255, 0, 0, 128);
    var bytes = Rgb5A3.Encode(fixture);
    Check(bytes.Length == 2048, "Icon byte count");
    Check(Word(bytes, 0) == 0xfc00 && Word(bytes, 1) == 0x83e0 && Word(bytes, 4) == 0x801f, "RGB555 / big endian");
    Check(Word(bytes, 2) == 0x4f00 && Word(bytes, 3) == 0, "Alpha / transparent pixel");
    Check(Word(bytes, 16) == 0xffff && Word(bytes, 128) == 0x8000, "4x4 tile order");
}
var directory = Path.Combine(Path.GetTempPath(), "dnd-save-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    var input = Path.Combine(directory, "input.png");
    var output = Path.Combine(directory, "icon.rgb5a3");
    using (var image = new Image<Rgba32>(64, 32, new Rgba32(255, 0, 0, 255))) image.SaveAsPng(input);
    Check(Run(input, output) == 0, "PNG conversion");
    var bytes = File.ReadAllBytes(output);
    Check(bytes.Length == 2048 && Word(bytes, 0) == 0 && Word(bytes, 32 * 8) == 0xfc00, "Aspect preserving transparent padding");
    Check(Run(input, output) == 1 && bytes.SequenceEqual(File.ReadAllBytes(output)), "No accidental overwrite");
    Check(Run(input, output, "--fit", "stretch", "--force") == 0 && Word(File.ReadAllBytes(output), 0) == 0xfc00, "Stretch and force");
    Check(Run(input, output, "--fit", "crop", "--force") == 0 && Word(File.ReadAllBytes(output), 0) == 0xfc00, "Crop");
    Check(Run(input, output, "--banner", "--force") == 0 && File.ReadAllBytes(output).Length == 6144, "Banner");
    var previous = File.ReadAllBytes(output);
    Check(Run(input, input, "--force") == 1, "Never overwrite input");
    File.WriteAllText(input, "not a PNG");
    Check(Run(input, output, "--force") == 1 && previous.SequenceEqual(File.ReadAllBytes(output)), "Invalid PNG preserves destination");
    Check(Run(input, output, "--fit", "bad") == 2 && Run(input, output, "--unknown") == 2 && Run() == 2, "CLI validation");
    Check(Run("--help") == 0, "Help");
}
finally { Directory.Delete(directory, true); }
Console.WriteLine("Save-image PNG conversion, sizing, tile order, alpha and CLI tests passed.");
