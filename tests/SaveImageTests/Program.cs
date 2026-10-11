using DolphinDotNet.Imaging;
using DolphinDotNet.SaveImage;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
static ushort Word(byte[] bytes, int pixel) => (ushort)((bytes[pixel * 2] << 8) | bytes[pixel * 2 + 1]);
static int Run(params string[] args) => Command.Run(args, new StringWriter(), new StringWriter());

using (var fixture = new RasterImage(32, 32))
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
    using var encoded = new MemoryStream();
    fixture.SaveAsPng(encoded);
    using var decoded = RasterImage.Load(encoded.ToArray(), pngOnly: true);
    Check(decoded[2, 0] == fixture[2, 0], "Straight alpha survives PNG decoding");
    try { _ = fixture[-1, 0]; throw new Exception("Invalid pixel accepted"); }
    catch (ArgumentOutOfRangeException) { }
}
try { using var oversized = new RasterImage(4097, 4096); throw new Exception("Oversized image accepted"); }
catch (InvalidDataException) { }
var directory = Path.Combine(Path.GetTempPath(), "dnd-save-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    var input = Path.Combine(directory, "input.png");
    var output = Path.Combine(directory, "icon.rgb5a3");
    using (var image = new RasterImage(64, 32, new Rgba32(255, 0, 0, 255))) image.SaveAsPng(input);
    Check(Run(input, output) == 0, "PNG conversion");
    var bytes = File.ReadAllBytes(output);
    Check(bytes.Length == 2048 && Word(bytes, 0) == 0 && Word(bytes, 32 * 8) == 0xfc00, "Aspect preserving transparent padding");
    Check(Run(input, output) == 1 && bytes.SequenceEqual(File.ReadAllBytes(output)), "No accidental overwrite");
    Check(Run(input, output, "--fit", "stretch", "--force") == 0 && Word(File.ReadAllBytes(output), 0) == 0xfc00, "Stretch and force");
    Check(Run(input, output, "--fit", "crop", "--force") == 0 && Word(File.ReadAllBytes(output), 0) == 0xfc00, "Crop");
    Check(Run(input, output, "--banner", "--force") == 0 && File.ReadAllBytes(output).Length == 6144, "Banner");
    var sourceFile = Path.Combine(directory, "SaveImages.cs");
    Check(Run(input, sourceFile, "--source", "--namespace", "Game.Assets", "--member", "SaveIcon") == 0, "Source output");
    var sourceText = File.ReadAllText(sourceFile);
    Check(sourceText.Contains("namespace @Game.@Assets") && sourceText.Contains("byte[] @SaveIcon => new byte[]"), "C# declaration");
    var literals = System.Text.RegularExpressions.Regex.Matches(sourceText, "0x([0-9A-F]{2})").Select(m => Convert.ToByte(m.Groups[1].Value, 16)).ToArray();
    Check(literals.SequenceEqual(RgbIcon(input)), "Embedded bytes equal binary output");
    Check(Run(input, sourceFile, "--source", "--class", "bad-name", "--force") == 1, "Source injection rejected");
    var discFile = Path.Combine(directory, "opening.bnr");
    Check(Run(input, discFile, "--disc-banner", "--title", "My Game", "--maker", "Studio", "--description", "Hello") == 0, "BNR1 conversion");
    var discBytes = File.ReadAllBytes(discFile);
    Check(discBytes.Length == 0x1960 && System.Text.Encoding.ASCII.GetString(discBytes, 0, 4) == "BNR1", "BNR1 header/size");
    Check(discBytes.AsSpan(4, 28).ToArray().All(b => b == 0) && System.Text.Encoding.Latin1.GetString(discBytes, 0x1820, 7) == "My Game" && discBytes[0x1827] == 0, "BNR layout and termination");
    Check(Run(input, discFile, "--disc-banner", "--bnr2", "--title", "PAL", "--force") == 0, "BNR2 conversion");
    discBytes = File.ReadAllBytes(discFile);
    Check(discBytes.Length == 0x1fa0 && discBytes[3] == (byte)'2', "BNR2 header/size");
    for (int i = 0; i < 6; i++) Check(System.Text.Encoding.ASCII.GetString(discBytes, 0x1820 + i * 0x140, 3) == "PAL", "BNR2 language blocks");
    Check(Run(input, discFile, "--disc-banner", "--title", new string('x',32), "--force") == 1, "Metadata overflow rejected");
    Check(Run(input, discFile, "--disc-banner", "--title", "日本", "--force") == 1, "Unsupported text encoding rejected");
    Check(Run(input, discFile, "--bnr2") == 2, "Invalid mode combination");
    var previous = File.ReadAllBytes(output);
    Check(Run(input, input, "--force") == 1, "Never overwrite input");
    File.WriteAllText(input, "not a PNG");
    Check(Run(input, output, "--force") == 1 && previous.SequenceEqual(File.ReadAllBytes(output)), "Invalid PNG preserves destination");
    Check(Run(input, output, "--fit", "bad") == 2 && Run(input, output, "--unknown") == 2 && Run() == 2, "CLI validation");
    Check(Run("--help") == 0, "Help");
}
finally { Directory.Delete(directory, true); }
Console.WriteLine("Save-image PNG conversion, sizing, tile order, alpha and CLI tests passed.");

byte[] RgbIcon(string input)
{
    var path = Path.Combine(directory, "comparison.rgb5a3");
    Check(Run(input, path) == 0, "Comparison output");
    return File.ReadAllBytes(path);
}

if (args.Length == 2 && args[0] == "--emit-source")
{
    Directory.CreateDirectory(args[1]);
    var embedded = Enumerable.Range(0, 2048).Select(i => (byte)i).ToArray();
    File.WriteAllText(Path.Combine(args[1], "SaveImages.cs"), SourceOutput.Generate(embedded, "Assets", "SaveImages", "Icon"));
    File.WriteAllText(Path.Combine(args[1], "Program.cs"), "class Program { static int Main() { var bytes = Assets.SaveImages.Icon; return bytes.Length == 2048 && bytes[0] == 0 && bytes[1] == 1 && bytes[2047] == 255 ? 0 : 1; } }");
    var core = Path.GetFullPath("src/DolphinDotNet.Core/DolphinDotNet.Core.csproj");
    File.WriteAllText(Path.Combine(args[1], "Embedded.csproj"), $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup><ItemGroup><ProjectReference Include=\"{System.Security.SecurityElement.Escape(core)}\" /></ItemGroup></Project>");
}
