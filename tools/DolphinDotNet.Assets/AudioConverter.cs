using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DolphinDotNet.Assets;

public sealed class AudioLoopSidecar
{
    public int Version { get; set; }
    public string? DefaultLoop { get; set; }
    public List<AudioLoopRange> Loops { get; set; } = new();
}

public sealed class AudioLoopRange
{
    public string Name { get; set; } = "";
    public decimal StartSeconds { get; set; }
    public decimal EndSeconds { get; set; }
}

public sealed record ResolvedAudioLoop(string Name, int StartSample, int EndSample);

public static class AudioConverter
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };

    // Positions count sample frames (one sample per channel); end is exclusive.
    public static ResolvedAudioLoop[] Resolve(AudioLoopSidecar sidecar, int sampleRate, int sampleCount)
    {
        if (sidecar.Version != 1) throw new InvalidDataException("Loop sidecar version must be 1.");
        if (sampleRate < 8000 || sampleRate > 48000 || sampleCount <= 0)
            throw new InvalidDataException("Invalid audio rate or sample count.");
        if (sidecar.Loops == null) throw new InvalidDataException("loops must be an array.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<ResolvedAudioLoop>();
        foreach (var range in sidecar.Loops)
        {
            if (range == null || string.IsNullOrWhiteSpace(range.Name) || !names.Add(range.Name))
                throw new InvalidDataException("Loop names must be nonempty and unique (case-sensitive).");
            if (range.StartSeconds < 0 || range.EndSeconds <= range.StartSeconds ||
                range.EndSeconds > (decimal)sampleCount / sampleRate)
                throw new InvalidDataException("Loop outside decoded audio: " + range.Name);
            int start = checked((int)decimal.Round(range.StartSeconds * sampleRate, 0, MidpointRounding.AwayFromZero));
            int end = checked((int)decimal.Round(range.EndSeconds * sampleRate, 0, MidpointRounding.AwayFromZero));
            if (start >= end) throw new InvalidDataException("Loop shorter than one sample: " + range.Name);
            result.Add(new(range.Name, start, end));
        }
        if (sidecar.DefaultLoop != null && !names.Contains(sidecar.DefaultLoop))
            throw new InvalidDataException("defaultLoop does not name a loop.");
        return result.ToArray();
    }

    public static AudioLoopSidecar ReadSidecar(string path)
    {
        if (!path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            return JsonSerializer.Deserialize<AudioLoopSidecar>(File.ReadAllText(path), Json)
                ?? throw new InvalidDataException("Missing loop sidecar object.");
        var sidecar = new AudioLoopSidecar { Version = 1 };
        int lineNumber = 0;
        foreach (string line in File.ReadLines(path))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            var fields = line.Split('\t', 3);
            if (fields.Length != 3 ||
                !decimal.TryParse(fields[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var start) ||
                !decimal.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var end))
                throw new InvalidDataException("Invalid Audacity region label on line " + lineNumber);
            // Point labels are markers, not loop regions. Spectral continuation rows
            // are deliberately rejected instead of interpreting them as time ranges.
            if (start == end) continue;
            sidecar.Loops.Add(new() { Name = fields[2], StartSeconds = start, EndSeconds = end });
        }
        return sidecar;
    }

    public static void Convert(string input, string output, string? sidecarPath = null,
        string? selectedLoop = null, int sampleRate = 32000, string ffmpeg = "ffmpeg")
    {
        input = Path.GetFullPath(input);
        output = Path.GetFullPath(output);
        sidecarPath = Path.GetFullPath(sidecarPath ?? input + ".loops.json");
        if (!output.EndsWith(".adx", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Output must use .adx.");
        if (sampleRate < 8000 || sampleRate > 48000) throw new ArgumentException("Sample rate must be 8000–48000.");
        if (output == input || output == sidecarPath || output + ".loops.json" == sidecarPath)
            throw new ArgumentException("Output must not overwrite an input.");
        var sidecar = ReadSidecar(sidecarPath);
        string temporary = Path.Combine(Path.GetTempPath(), "dnd-audio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string pcm = Path.Combine(temporary, "audio.pcm"), adx = Path.Combine(temporary, "audio.adx");
            // Decode once, including MP3 gapless trimming, then author ranges on that timeline.
            Run(ffmpeg, "-nostdin", "-hide_banner", "-loglevel", "error", "-y", "-i", input,
                "-map", "0:a:0", "-vn", "-ar", sampleRate.ToString(CultureInfo.InvariantCulture), "-ac", "2",
                "-c:a", "pcm_s16le", "-f", "s16le", pcm);
            int count = checked((int)(new FileInfo(pcm).Length / 4));
            var loops = Resolve(sidecar, sampleRate, count);
            string? selection = selectedLoop ?? sidecar.DefaultLoop;
            var loop = selection == null ? null : loops.SingleOrDefault(x => x.Name == selection)
                ?? throw new InvalidDataException("Unknown loop: " + selection);
            Run(ffmpeg, "-nostdin", "-hide_banner", "-loglevel", "error", "-y", "-f", "s16le",
                "-ar", sampleRate.ToString(CultureInfo.InvariantCulture), "-ac", "2", "-i", pcm,
                "-c:a", "adpcm_adx", "-f", "adx", adx);
            byte[] encoded = AddLoopHeader(File.ReadAllBytes(adx), count, loop);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            // Validate everything before publishing either output.
            string staged = Path.Combine(temporary, "ranges.json");
            File.WriteAllText(staged, JsonSerializer.Serialize(new
            {
                version = 1, sampleRate, sampleCount = count, defaultLoop = selection, loops
            }, Json));
            File.WriteAllBytes(adx, encoded);
            File.Copy(staged, output + ".loops.json", true);
            File.Copy(adx, output, true);
        }
        finally { Directory.Delete(temporary, true); }
    }

    // FFmpeg emits unencrypted ADX v3, 18-byte blocks, 32 sample frames per block.
    // Rebuild the header with room for the standard single-loop structure.
    private static byte[] AddLoopHeader(byte[] source, int count, ResolvedAudioLoop? loop)
    {
        if (source.Length < 36 || BinaryPrimitives.ReadUInt16BigEndian(source) != 0x8000 ||
            source[4] != 3 || source[5] != 18 || source[6] != 4 || source[7] != 2 ||
            BinaryPrimitives.ReadUInt16BigEndian(source.AsSpan(18)) != 0x0300)
            throw new InvalidDataException("FFmpeg returned an unsupported ADX header.");
        int oldStart = BinaryPrimitives.ReadUInt16BigEndian(source.AsSpan(2)) + 4;
        if (oldStart < 26 || oldStart > source.Length ||
            !source.AsSpan(oldStart - 6, 6).SequenceEqual("(c)CRI"u8) ||
            source.Length - oldStart < ((long)count + 31) / 32 * 36)
            throw new InvalidDataException("Truncated ADX stream.");
        const int dataStart = 64;
        var result = new byte[checked(dataStart + source.Length - oldStart)];
        source.AsSpan(0, 20).CopyTo(result);
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(2), dataStart - 4);
        Write(12, count); // Exclude final codec-block padding from the logical duration.
        "(c)CRI"u8.CopyTo(result.AsSpan(dataStart - 6));
        source.AsSpan(oldStart).CopyTo(result.AsSpan(dataStart));
        if (loop != null)
        {
            BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(22), 1);
            Write(24, 1);
            Write(28, loop.StartSample);
            Write(32, checked(dataStart + loop.StartSample / 32 * 36));
            Write(36, loop.EndSample);
            Write(40, checked(dataStart + (int)(((long)loop.EndSample + 31) / 32 * 36)));
        }
        return result;
        void Write(int offset, int value) => BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(offset), value);
    }

    private static void Run(string executable, params string[] arguments)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new IOException("Could not start FFmpeg.");
        var stderr = process.StandardError.ReadToEndAsync();
        var stdout = process.StandardOutput.ReadToEndAsync();
        if (!process.WaitForExit(300000)) { process.Kill(true); process.WaitForExit(); throw new IOException("FFmpeg timed out."); }
        Task.WaitAll(stderr, stdout);
        if (process.ExitCode != 0) throw new InvalidDataException("FFmpeg failed: " + stderr.Result);
    }
}
