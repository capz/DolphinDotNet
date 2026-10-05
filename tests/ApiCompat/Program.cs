using System.Diagnostics;
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
    File.Copy(referencePath, Path.Combine(scratch, "reference.dll"));
    Require(ApiSurface.Read(scratch).SetEquals(reference), "Directory scanning changed the contract.");
}
finally
{
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
    process.WaitForExit();
    return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
}