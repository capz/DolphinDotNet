using System.Diagnostics;
using System.Text.Json;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DolphinDotNet.ApiCompat;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
#if DEBUG
const string configuration = "Debug";
#else
const string configuration = "Release";
#endif
var referencePath = Path.Combine(root, $"tests/ApiCompat/Reference/bin/{configuration}/net8.0/Reference.dll");
var implementationPath = Path.Combine(root, $"tests/ApiCompat/Implementation/bin/{configuration}/net8.0/Implementation.dll");
var reference = ApiSurface.Read(referencePath);
var implementation = ApiSurface.Read(implementationPath);
var missing = reference.Except(implementation).Order().ToArray();
Require(!RawEchoSignature(referencePath).SequenceEqual(RawEchoSignature(implementationPath)), "Fixtures did not actually shift signature tokens.");
Require(missing.Length == 5, $"Expected five real mismatches, got {missing.Length}:\n{string.Join('\n', missing)}");
Require(missing.Any(x => x.StartsWith("M:ContractFixture.Surface::Changed:")), "Changed parameter/return type was missed.");
Require(missing.Any(x => x.StartsWith("M:ContractFixture.Surface::Storage:instance")), "Instance/static mismatch was missed.");
Require(missing.Any(x => x.StartsWith("E:ContractFixture.Surface::ChangedEvent:")), "Changed event type was missed.");
Require(reference.Any(x => x.StartsWith("M:ContractFixture.Surface::Protected:")), "Protected API was excluded.");
Require(reference.Contains("T:ContractFixture.Surface+Nested"), "Nested type identity was lost.");
Require(!reference.Any(x => x.Contains("LeakedNested") || x.Contains("HiddenProperty") || x.Contains("HiddenEvent")), "Private API leaked into coverage.");
Require(reference.Any(x => x.Contains("!!0")), "Generic parameters were not decoded.");
Require(reference.Any(x => x.Contains("System.Collections.Generic.List`1<ContractFixture.Item[]>")), "Generic array signature was not decoded.");
Require(reference.Any(x => x.Contains("ContractFixture.Item&") && x.Contains("rank=2")), "Byref/array rank was not decoded.");

var scratch = Path.Combine(Path.GetTempPath(), "dnd-api-compat-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);
try
{
    var characterized = Path.Combine(scratch, "characterized.txt");
    File.WriteAllLines(characterized, reference);
    var result = Run(referencePath, implementationPath, characterized);
    Require(result.ExitCode == 1, "Missing APIs must return status 1.");
    Require(result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == 5, "CLI missing-API output differs from inventory.");
    Require(result.Error.Contains($"Runtime-characterized: {reference.Count - missing.Length};"), "Characterized coverage includes absent implementation APIs.");
    Require(Run(referencePath, referencePath).ExitCode == 0, "Matching contracts must return status 0.");
    Require(Run(scratch, implementationPath).ExitCode == 2, "Empty contract directories must return status 2.");
    var invalid = Path.Combine(scratch, "invalid.dll");
    File.WriteAllText(invalid, "not a managed assembly");
    Require(Run(invalid, implementationPath).ExitCode == 2, "Invalid assemblies must return status 2.");
    File.Delete(invalid);
    var packages = Path.Combine(scratch, "packages");
    var selected = Path.Combine(packages, "fixture", "1.0", "ref", "net8.0");
    var unselected = Path.Combine(packages, "fixture", "2.0", "ref", "net8.0");
    Directory.CreateDirectory(selected);
    Directory.CreateDirectory(unselected);
    File.Copy(referencePath, Path.Combine(selected, "Contract.dll"));
    File.Copy(implementationPath, Path.Combine(unselected, "Contract.dll"));
    var assets = Path.Combine(scratch, "project.assets.json");
    File.WriteAllText(assets, JsonSerializer.Serialize(new
    {
        targets = new Dictionary<string, object>
        {
            ["netstandard1.0"] = new Dictionary<string, object>
            {
                ["Fixture/1.0"] = new { type = "package", compile = new Dictionary<string, object> { ["ref/net8.0/Contract.dll"] = new { } } }
            }
        },
        libraries = new Dictionary<string, object> { ["Fixture/1.0"] = new { path = "fixture/1.0" } },
        packageFolders = new Dictionary<string, object> { [packages] = new { } }
    }));
    Require(ContractAssets.Read(assets).SetEquals(reference), "Unselected cached package versions polluted the contract.");
    Require(Run("--assets", assets, referencePath).ExitCode == 0, "Resolved-assets CLI scan failed.");
    File.Delete(Path.Combine(selected, "Contract.dll"));
    Require(Run("--assets", assets, referencePath).ExitCode == 2, "Missing resolved assets must return status 2.");
    File.WriteAllText(assets, "{}");
    Require(Run("--assets", assets, referencePath).ExitCode == 2, "Invalid assets shape must return status 2.");
    File.WriteAllText(assets, "{broken");
    Require(Run("--assets", assets, referencePath).ExitCode == 2, "Malformed assets JSON must return status 2.");
    // Test directory union with just the contract, avoiding the deliberately different fixture.
    var contractDirectory = Path.Combine(scratch, "contract");
    Directory.CreateDirectory(contractDirectory);
    File.Copy(referencePath, Path.Combine(contractDirectory, "reference.dll"));
    Require(ApiSurface.Read(contractDirectory).SetEquals(reference), "Directory scanning changed the contract.");
}
finally
{
    var expectedParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
    Require(Path.GetDirectoryName(Path.GetFullPath(scratch)) == expectedParent, "Unexpected scratch cleanup path.");
    Directory.Delete(scratch, recursive: true);
}
Console.WriteLine($"API compatibility tests passed: {reference.Count} reference declarations; five intentional differences; metadata-token shifts ignored.");

static void Require(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static (int ExitCode, string Output, string Error) Run(params string[] arguments)
{
    var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
    start.ArgumentList.Add(typeof(ApiSurface).Assembly.Location);
    foreach (var argument in arguments) start.ArgumentList.Add(argument);
    using var process = Process.Start(start) ?? throw new Exception("Could not launch scanner.");
    var output = process.StandardOutput.ReadToEndAsync();
    var error = process.StandardError.ReadToEndAsync();
    if (!process.WaitForExit(30_000))
    {
        process.Kill(entireProcessTree: true);
        throw new Exception("Scanner test timed out.");
    }
    return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
}
static byte[] RawEchoSignature(string path)
{
    using var stream = File.OpenRead(path);
    using var pe = new PEReader(stream);
    var metadata = pe.GetMetadataReader();
    var type = metadata.TypeDefinitions.Select(metadata.GetTypeDefinition)
        .Single(x => metadata.GetString(x.Name) == "Surface");
    var method = type.GetMethods().Select(metadata.GetMethodDefinition)
        .Single(x => metadata.GetString(x.Name) == "Echo");
    return metadata.GetBlobBytes(method.Signature);
}