using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

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
    !generated.Contains("0x06, 0x02"))
    throw new Exception("Compiler output did not contain expected strings/internal calls.");

File.Delete(temp);

var genericProject = Path.Combine(root, "tests/GenericSharingSmoke/GenericSharingSmoke.csproj");
Run("dotnet", $"build \"{genericProject}\" -c Release");
var genericDll = Path.Combine(root, "tests/GenericSharingSmoke/bin/Release/net8.0/GenericSharingSmoke.dll");
var genericOutput = Path.Combine(Path.GetTempPath(), $"dnd-generic-{Guid.NewGuid():N}.c");
Run("dotnet", $"run --project \"{compiler}\" -- --aot \"{genericDll}\" \"{genericOutput}\"");
var genericGenerated = File.ReadAllText(genericOutput);
var sharedDefinitions = genericGenerated.Split('\n')
    .Count(line => line.Contains("Shared_1_Marker", StringComparison.Ordinal) && line.TrimEnd().EndsWith("{", StringComparison.Ordinal));
if (sharedDefinitions != 1)
    throw new Exception($"Expected one shared generic method body, found {sharedDefinitions}.");
File.Delete(genericOutput);
Console.WriteLine("DolphinDotNet compiler integration test passed.");

static void Run(string file, string args)
{
    using var p = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false })
        ?? throw new Exception($"Could not start {file}.");
    p.WaitForExit();
    if (p.ExitCode != 0) throw new Exception($"{file} {args} failed with exit code {p.ExitCode}.");
}
