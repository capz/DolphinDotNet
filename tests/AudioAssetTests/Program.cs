using DolphinDotNet.Assets;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;

void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
void Reject(AudioLoopSidecar sidecar)
{
    try { AudioConverter.Resolve(sidecar, 32000, 64000); }
    catch (InvalidDataException) { return; }
    throw new Exception("Invalid sidecar accepted.");
}
var valid = new AudioLoopSidecar { Version = 1, DefaultLoop = "main", Loops = new()
{
    new() { Name = "main", StartSeconds = 0.125m, EndSeconds = 1.5m },
    new() { Name = "alt", StartSeconds = 0.25001m, EndSeconds = 1.75m }
}};
var resolved = AudioConverter.Resolve(valid, 32000, 64000);
Check(resolved[0].StartSample == 4000 && resolved[0].EndSample == 48000, "Time conversion");
Reject(new() { Version = 2 });
Reject(new() { Version = 1, DefaultLoop = "absent" });
foreach (var range in new AudioLoopRange[] {
    new() { Name="x", StartSeconds=-1, EndSeconds=1 },
    new() { Name="x", StartSeconds=1, EndSeconds=1 },
    new() { Name="x", EndSeconds=3 }, new() { Name="", EndSeconds=1 },
    new() { Name="x", EndSeconds=0.000001m } })
    Reject(new() { Version = 1, Loops = new() { range } });
Reject(new() { Version=1, Loops=new() { new() { Name="x", EndSeconds=1 }, new() { Name="x", EndSeconds=2 } } });

string dir = Path.Combine(Path.GetTempPath(), "dnd-audio-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(dir);
try
{
    void Run(params string[] args)
    {
        var psi = new ProcessStartInfo("ffmpeg") { UseShellExecute = false };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var p = Process.Start(psi)!;
        p.WaitForExit(); Check(p.ExitCode == 0, "FFmpeg fixture/verification failed");
    }
    string mp3 = Path.Combine(dir, "track with spaces.mp3"), output = Path.Combine(dir, "track.adx");
    Run("-v", "error", "-f", "lavfi", "-i", "sine=frequency=440:duration=2", mp3);
    string json = """
    {"version":1,"defaultLoop":"main","loops":[
      {"name":"main","startSeconds":0.125,"endSeconds":1.5},
      {"name":"alt","startSeconds":0.25,"endSeconds":1.75}]}
    """;
    File.WriteAllText(mp3 + ".loops.json", json);
    string labels = Path.Combine(dir, "regions.txt");
    File.WriteAllText(labels, "0.125\t1.5\tmain\n0.25\t1.75\talt\n0\t0\tmarker\n");
    var imported = AudioConverter.ReadSidecar(labels);
    Check(imported.Loops.Count == 2 && imported.Loops[1].Name == "alt", "Audacity region import");
    AudioConverter.Convert(mp3, output, labels, "main");
    AudioConverter.Convert(mp3, output);
    byte[] adx = File.ReadAllBytes(output);
    int Read(int offset) => BinaryPrimitives.ReadInt32BigEndian(adx.AsSpan(offset));
    Check(Read(24) == 1 && Read(28) == 4000 && Read(36) == 48000, "Embedded loop");
    Check(Read(12) == 64000, "MP3 gapless duration or ADX padding count");
    Check(Read(32) == 64 + 4000 / 32 * 36 && Read(40) == 64 + 48000 / 32 * 36, "ADX loop offsets");
    using (var meta = JsonDocument.Parse(File.ReadAllText(output + ".loops.json")))
        Check(meta.RootElement.GetProperty("loops").GetArrayLength() == 2, "Lost names");
    Run("-v", "error", "-i", output, "-f", "null", "-");
    AudioConverter.Convert(mp3, output, selectedLoop: "alt", sampleRate: 48000);
    adx = File.ReadAllBytes(output);
    Check(Read(28) == 12000 && Read(36) == 84000, "Override/resampling");
    File.WriteAllText(mp3 + ".loops.json", "{\"version\":1,\"loops\":[]}");
    AudioConverter.Convert(mp3, output);
    adx = File.ReadAllBytes(output); Check(Read(24) == 0, "Unexpected loop");
    byte[] previous = adx;
    File.WriteAllText(mp3 + ".loops.json", json.Replace("1.75", "100"));
    try { AudioConverter.Convert(mp3, output); throw new Exception("Out-of-bounds conversion accepted"); }
    catch (InvalidDataException) { }
    Check(File.ReadAllBytes(output).SequenceEqual(previous), "Invalid input overwrote output");
}
finally { Directory.Delete(dir, true); }
Console.WriteLine("Audio asset tests passed.");
