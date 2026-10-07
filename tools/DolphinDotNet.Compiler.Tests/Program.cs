using System;
using System.Diagnostics;
using System.IO;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var sample = Path.Combine(root, "samples/HelloGameCube/HelloGameCube.csproj");
var compiler = Path.Combine(root, "tools/DolphinDotNet.Compiler/DolphinDotNet.Compiler.csproj");
var temp = Path.Combine(Path.GetTempPath(), $"dnd-{Guid.NewGuid():N}.h");

Run("dotnet", $"build \"{sample}\" -c Release");
var dll = Path.Combine(root, "samples/HelloGameCube/bin/Release/net8.0/HelloGameCube.dll");
Run("dotnet", $"run --project \"{compiler}\" -- \"{dll}\" \"{temp}\"");

var generated = File.ReadAllText(temp);
if (!generated.Contains("Hello from real C#!") ||
    !generated.Contains("0x06, 0x01") ||
    !generated.Contains("0x06, 0x02") ||
    !generated.Contains("Shared generic AOT body."))
    throw new Exception("Compiler output did not contain expected strings/internal calls.");

if (CountOccurrences(generated, "Shared generic AOT body.") != 1)
    throw new Exception("Closed generic instantiations emitted duplicate shared generic data/code.");

File.Delete(temp);
Console.WriteLine("DolphinDotNet compiler integration test passed.");

static void Run(string file, string args)
{
    using var p = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false })
        ?? throw new Exception($"Could not start {file}.");
    p.WaitForExit();
    if (p.ExitCode != 0) throw new Exception($"{file} {args} failed with exit code {p.ExitCode}.");
}

static int CountOccurrences(string text, string value)
{
    var count = 0;
    for (var index = 0; (index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length) count++;
    return count;
}
