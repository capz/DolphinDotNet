using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var sample = Path.Combine(root, "samples/HelloGameCube/HelloGameCube.csproj");
var compiler = Path.Combine(root, "tools/DolphinDotNet.Compiler/DolphinDotNet.Compiler.csproj");
var tempDirectory = Path.Combine(Path.GetTempPath(), $"dnd-{Guid.NewGuid():N}");
var temp = Path.Combine(tempDirectory, "hello.c");

Run("dotnet", $"build \"{sample}\" -c Release -p:DndBuildNative=false");
var dll = Path.Combine(root, "samples/HelloGameCube/bin/Release/net8.0/HelloGameCube.dll");
Run("dotnet", $"run --project \"{compiler}\" -- \"{dll}\" \"{temp}\"");

var generated = File.ReadAllText(temp);
if (!generated.Contains("dnd_console_write_line") ||
    !generated.Contains("dnd_aot_entry"))
    throw new Exception("Compiler output did not contain native AOT calls.");

Run("dotnet", $"run --project \"{compiler}\" -- --aot \"{dll}\" \"{temp}\"");
if (File.ReadAllText(temp) != generated)
    throw new Exception("Default compilation and --aot produced different native programs.");

var helloHost = Path.Combine(tempDirectory, "hello-host");
Run("cc", $"-pthread -std=c11 -Wall -Wextra -Werror -I\"{Path.Combine(root, "include")}\" \"{temp}\" \"{Path.Combine(root, "tests/hello_gamecube_host.c")}\" \"{Path.Combine(root, "source/managed_runtime.c")}\" \"{Path.Combine(root, "source/async_runtime.c")}\" -o \"{helloHost}\"");
Run(helloHost, "");
// Normal Release output must not link libdb or install a trap.
// Debugger acceptance uses a separate managed Debug build.
var normalBundle = Path.Combine(tempDirectory, "normal-bundle");
Run("dotnet", $"run --project \"{compiler}\" -- --bundle \"{dll}\" \"{normalBundle}\"");
var normalMake = File.ReadAllText(Path.Combine(normalBundle, "Makefile"));
foreach (var dependency in new[] { "s2d.c", "d2fixed.c", "LICENSE-Boost" })
    if (!File.Exists(Path.Combine(normalBundle, "include", "ryu", dependency)))
        throw new Exception("Standalone bundle is missing numeric dependency: " + dependency);
if (normalMake.Contains("-ldb ", StringComparison.Ordinal) ||
    normalMake.Contains("DND_GDB_ENABLED", StringComparison.Ordinal) ||
    File.Exists(Path.Combine(normalBundle, "source/debug_gamecube.c")))
    throw new Exception("Normal bundle unexpectedly enables the GDB debugger.");

if (normalMake.Contains("-DDND_SAMPLE_CONTROLS", StringComparison.Ordinal))
    throw new Exception("Application bundles must opt in to sample controls.");
var sampleBundle = Path.Combine(tempDirectory, "sample-controls");
Run("dotnet", $"run --project \"{compiler}\" -- --bundle \"{dll}\" \"{sampleBundle}\" --sample-controls");
if (!File.ReadAllText(Path.Combine(sampleBundle, "Makefile")).Contains("-DDND_SAMPLE_CONTROLS", StringComparison.Ordinal))
    throw new Exception("Sample controls did not reach the native build.");

Run("dotnet", $"build \"{sample}\" -c Debug -p:DndBuildNative=false -p:DndDebugSymbols=true");
var debugDll = Path.Combine(root, "samples/HelloGameCube/bin/Debug/net8.0/HelloGameCube.dll");

var symbolsBundle = Path.Combine(tempDirectory, "symbols-only");
Run("dotnet", $"run --project \"{compiler}\" -- --bundle \"{debugDll}\" \"{symbolsBundle}\" --debug-symbols");
var symbolsMake = File.ReadAllText(Path.Combine(symbolsBundle, "Makefile"));
var symbolsC = File.ReadAllText(Path.Combine(symbolsBundle, "source/generated_program.c"));
if (symbolsMake.Contains("-ldb ", StringComparison.Ordinal) ||
    symbolsMake.Contains("DND_GDB_ENABLED", StringComparison.Ordinal) ||
    File.Exists(Path.Combine(symbolsBundle, "source/debug_gamecube.c")) ||
    !symbolsMake.Contains("-Og -g3 -gdwarf-4", StringComparison.Ordinal) ||
    !symbolsMake.Contains("-fno-optimize-sibling-calls", StringComparison.Ordinal) ||
    !symbolsC.Contains("#line 8 ", StringComparison.Ordinal) ||
    !File.Exists(Path.Combine(symbolsBundle, "dnd-il-debug.json")))
    throw new Exception("Emulator symbols-only build links a device debugger or lacks source locations.");

var usbBundle = Path.Combine(tempDirectory, "usb-gecko-bundle");
Run("dotnet", $"run --project \"{compiler}\" -- --bundle \"{debugDll}\" \"{usbBundle}\" --gdb-usb 1");
var usbMake = File.ReadAllText(Path.Combine(usbBundle, "Makefile"));
var usbC = File.ReadAllText(Path.Combine(usbBundle, "source/debug_gamecube.c"));
if (!usbMake.Contains("-ldb ", StringComparison.Ordinal) ||
    !usbMake.Contains("DND_GDB_ENABLED", StringComparison.Ordinal) ||
    !usbC.Contains("DEBUG_Init(GDBSTUB_DEVICE_USB, 1)", StringComparison.Ordinal) ||
    !usbC.Contains("_break()", StringComparison.Ordinal) ||
    !File.Exists(Path.Combine(usbBundle, "dnd-il-debug.json")) ||
    !usbMake.Contains("-Og -g3 -gdwarf-4", StringComparison.Ordinal))
    throw new Exception("USB Gecko bundle is missing debugger linkage, source symbols or EXI channel selection.");
if (usbC.Contains("DEBUG_Init(GDBSTUB_DEVICE_TCP", StringComparison.Ordinal) ||
    usbC.Contains("tcp_localip = \"192.0.2.10\"", StringComparison.Ordinal))
    throw new Exception("USB Gecko bundle unexpectedly starts the TCP debugger.");

var gdbBundle = Path.Combine(tempDirectory, "gdb-bundle");
Run("dotnet", $"run --project \"{compiler}\" -- --bundle \"{debugDll}\" \"{gdbBundle}\" --gdb-tcp 192.0.2.10 --gdb-netmask 255.255.255.0 --gdb-gateway 192.0.2.1 --gdb-port 2828");
var debugMake = File.ReadAllText(Path.Combine(gdbBundle, "Makefile"));
var debugC = File.ReadAllText(Path.Combine(gdbBundle, "source/debug_gamecube.c"));
var generatedGdbC = File.ReadAllText(Path.Combine(gdbBundle, "source/generated_program.c"));
if (!debugMake.Contains("-ldb ", StringComparison.Ordinal) ||
    !debugMake.Contains("-Og -g3 -gdwarf-4", StringComparison.Ordinal) ||
    !debugMake.Contains("DND_GDB_ENABLED", StringComparison.Ordinal) ||
    !debugMake.Contains("DND_NO_AUTONETWORK", StringComparison.Ordinal) ||
    !debugC.Contains("DEBUG_Init(GDBSTUB_DEVICE_TCP, 2828)", StringComparison.Ordinal) ||
    !debugC.Contains("tcp_localip = \"192.0.2.10\"", StringComparison.Ordinal) ||
    !debugC.Contains("_break()", StringComparison.Ordinal))
    throw new Exception("GDB bundle is missing native debug settings or startup rendezvous.");
if (!generatedGdbC.Contains("#line 7 ", StringComparison.Ordinal) ||
    !generatedGdbC.Contains("#line 8 ", StringComparison.Ordinal) ||
    !generatedGdbC.Contains("#line 9 ", StringComparison.Ordinal) ||
    !generatedGdbC.Contains("HelloGameCube/Program.cs", StringComparison.Ordinal) ||
    !generatedGdbC.Contains("DND_SOURCE_ANCHOR", StringComparison.Ordinal) ||
    generatedGdbC.Contains("#line 16707566 ", StringComparison.Ordinal))
    throw new Exception("GDB generated C is missing visible C# statement line directives.");
if (File.ReadAllText(Path.Combine(normalBundle, "source/generated_program.c"))
        .Contains("#line ", StringComparison.Ordinal) ||
    File.ReadAllText(Path.Combine(normalBundle, "source/generated_program.c"))
        .Contains("DND_SOURCE_ANCHOR", StringComparison.Ordinal))
    throw new Exception("Normal AOT output unexpectedly contains managed #line directives.");
if (File.Exists(Path.Combine(normalBundle, "dnd-il-debug.json")))
    throw new Exception("Default bundle unexpectedly contains debugger metadata.");
var debugMapPath = Path.Combine(gdbBundle, "dnd-il-debug.json");
if (!debugMake.Contains("-DDND_DEBUG_TELEMETRY", StringComparison.Ordinal) ||
    !debugMake.Contains("--undefined=dnd_debugger_telemetry", StringComparison.Ordinal) ||
    File.ReadAllText(Path.Combine(normalBundle, "Makefile")).Contains("-DDND_DEBUG_TELEMETRY", StringComparison.Ordinal) ||
    !debugC.Contains("dnd_performance_network(2, false, 0xc000020au, 0xffffff00u, 0xc0000201u)", StringComparison.Ordinal))
    throw new Exception("Debug overlay telemetry must be opt-in with symbols and report the separate TCP stub configuration.");
using (var debugMap = System.Text.Json.JsonDocument.Parse(File.ReadAllText(debugMapPath)))
{
    var telemetry = debugMap.RootElement.GetProperty("debuggerTelemetry");
    if (telemetry.GetProperty("nativeSymbol").GetString() != "dnd_debugger_telemetry" || telemetry.GetProperty("size").GetInt32() != 8)
        throw new Exception("Missing host-only debugger overlay telemetry metadata.");
    var heartbeat = debugMap.RootElement.GetProperty("debuggerHeartbeat");
    if (heartbeat.GetProperty("nativeSymbol").GetString() != "dnd_debugger_heartbeat" ||
        heartbeat.GetProperty("size").GetInt32() != 24 || heartbeat.GetProperty("supported").GetBoolean())
        throw new Exception("The native BBA GDB stack must explicitly decline application UDP heartbeat support.");
    if (!debugMake.Contains("--undefined=dnd_debugger_heartbeat", StringComparison.Ordinal) ||
        !File.Exists(Path.Combine(gdbBundle, "source/debug_heartbeat.c")))
        throw new Exception("Heartbeat metadata requires its retained native implementation.");
    if (debugMap.RootElement.GetProperty("schemaVersion").GetInt32() != 2)
        throw new Exception("Unexpected debug IL map schema.");
    if (!debugMap.RootElement.GetProperty("assemblies").EnumerateArray()
        .Any(module => module.GetProperty("name").GetString() == "HelloGameCube" &&
            module.GetProperty("pdbId").GetString()!.Length == 40 &&
            Guid.TryParse(module.GetProperty("moduleVersionId").GetString(), out _)))
        throw new Exception("Missing verified managed assembly / Portable PDB identity.");
    var methods = debugMap.RootElement.GetProperty("methods").EnumerateArray();
    bool entryFound = false;
    foreach (var method in methods)
    {
        if (method.GetProperty("name").GetString() != "Main" ||
            method.GetProperty("declaringType").GetString() != "Program")
            continue;
        var offsets = method.GetProperty("ilOffsets").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        if (offsets.Length < 2 || offsets[0] < 0 ||
            !offsets.SequenceEqual(offsets.Distinct().OrderBy(x => x)))
            throw new Exception("Managed entry method is missing deterministic per-instruction IL offsets.");
        if (!method.TryGetProperty("variables", out var variables) || variables.ValueKind != System.Text.Json.JsonValueKind.Array)
            throw new Exception("Primitive variable metadata missing from debug method.");
        var locations = method.GetProperty("sourceLocations").EnumerateArray().ToArray();
        var visibleLines = locations
            .Where(location => location.GetProperty("document").GetString()!
                .Replace('\\', '/').EndsWith("HelloGameCube/Program.cs", StringComparison.Ordinal))
            .Select(location => location.GetProperty("startLine").GetInt32())
            .Distinct().ToArray();
        if (visibleLines.Length < 3 || !visibleLines.Contains(7) || !visibleLines.Contains(8) ||
            !visibleLines.Contains(9))
            throw new Exception("Distinct C# statement locations were not recovered from the Portable PDB.");
        if (locations.Any(location => location.GetProperty("ilOffset").GetInt32() <
                location.GetProperty("sequencePointIlOffset").GetInt32()))
            throw new Exception("A mapped IL offset precedes its sequence point.");
        entryFound = true;
    }
    if (!entryFound) throw new Exception("Managed entry method absent from debug IL map.");
}
var inspectionProject = Path.Combine(root, "tests/DebuggerInspectionSmoke/DebuggerInspectionSmoke.csproj");
Run("dotnet", $"build \"{inspectionProject}\" -c Debug");
var inspectionDll = Path.Combine(root, "tests/DebuggerInspectionSmoke/bin/Debug/net8.0/DebuggerInspectionSmoke.dll");
var inspectionBundle = Path.Combine(tempDirectory, "inspection-bundle");
Run("dotnet", $"run --project \"{compiler}\" -- --bundle \"{inspectionDll}\" \"{inspectionBundle}\" --debug-symbols");
using (var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(inspectionBundle, "dnd-il-debug.json"))))
{
    var map = document.RootElement;
    var box = map.GetProperty("inspection").GetProperty("types").EnumerateArray().Single(t => t.GetProperty("name").GetString() == "Box");
    var fields = box.GetProperty("fields").EnumerateArray().ToDictionary(f => f.GetProperty("name").GetString()!);
    if (box.GetProperty("nativeSymbol").GetString() != "dnd_type_Box" || box.GetProperty("size").GetInt32() != 48 ||
        fields["Count"].GetProperty("offset").GetInt32() != 8 || fields["Flag"].GetProperty("storageBytes").GetInt32() != 1 ||
        fields["Name"].GetProperty("offset").GetInt32() != 16 || fields["Name"].GetProperty("storageBytes").GetInt32() != 4 ||
        fields["Delta"].GetProperty("offset").GetInt32() != 40)
        throw new Exception("Object inspection layout disagrees with generated inherited payload/PowerPC pointer widths.");
    var methods = map.GetProperty("methods").EnumerateArray().ToArray();
    var inspect = methods.Single(m => m.GetProperty("name").GetString() == "Inspect").GetProperty("variables").EnumerateArray().ToArray();
    if (!new[] { "box", "text", "local", "caption" }.All(name => inspect.Any(v => v.GetProperty("name").GetString() == name &&
            v.GetProperty("valueKind").GetString() == "reference" && v.GetProperty("storageBytes").GetInt32() == 4)) ||
        !methods.Single(m => m.GetProperty("name").GetString() == "Read").GetProperty("variables").EnumerateArray().Any(v => v.GetProperty("name").GetString() == "this"))
        throw new Exception("Reference arguments, locals or this are missing from debug metadata.");
}
// Missing and mismatched PDBs must fail rather than returning misleading
// source coordinates to the IDE; normal builds continue to ignore PDBs.
var staleDirectory = Path.Combine(tempDirectory, "stale-pdb");
Directory.CreateDirectory(staleDirectory);
foreach (var file in Directory.GetFiles(Path.GetDirectoryName(debugDll)!, "*.dll")
             .Concat(Directory.GetFiles(Path.GetDirectoryName(debugDll)!, "*.pdb")))
    File.Copy(file, Path.Combine(staleDirectory, Path.GetFileName(file)), true);
var staleDll = Path.Combine(staleDirectory, "HelloGameCube.dll");
File.Delete(Path.Combine(staleDirectory, "HelloGameCube.pdb"));
AssertPdbRejected(staleDll, Path.Combine(tempDirectory, "no-pdb"));
var unrelatedPdb = Directory.GetFiles(staleDirectory, "*.pdb")
    .First(path => !Path.GetFileName(path).Equals("HelloGameCube.pdb", StringComparison.OrdinalIgnoreCase));
File.Copy(unrelatedPdb, Path.Combine(staleDirectory, "HelloGameCube.pdb"));
AssertPdbRejected(staleDll, Path.Combine(tempDirectory, "wrong-pdb"));
// USB Gecko rejects invalid channels and contradictory TCP options before AOT.
RunFailure("dotnet", $"run --project \"{compiler}\" -- --bundle \"{dll}\" \"{Path.Combine(tempDirectory, "usb-invalid")}\" --gdb-usb 2");
RunFailure("dotnet", $"run --project \"{compiler}\" -- --bundle \"{dll}\" \"{Path.Combine(tempDirectory, "usb-tcp-mixed")}\" --gdb-usb 1 --gdb-tcp 192.0.2.10 --gdb-netmask 255.255.255.0 --gdb-gateway 192.0.2.1");
Directory.Delete(tempDirectory, recursive: true);

var genericProject = Path.Combine(root, "tests/GenericSharingSmoke/GenericSharingSmoke.csproj");
Run("dotnet", $"build \"{genericProject}\" -c Release");
var genericDll = Path.Combine(root, "tests/GenericSharingSmoke/bin/Release/net8.0/GenericSharingSmoke.dll");
Run("dotnet", $"\"{genericDll}\"");
var genericOutput = Path.Combine(Path.GetTempPath(), $"dnd-generic-{Guid.NewGuid():N}.c");
Run("dotnet", $"run --project \"{compiler}\" -- --aot \"{genericDll}\" \"{genericOutput}\"");
var genericGenerated = File.ReadAllText(genericOutput);
var genericObject = Path.Combine(Path.GetTempPath(), $"dnd-generic-{Guid.NewGuid():N}.o");
Run("cc", $"-std=c11 -Wall -Wextra -Werror -I\"{Path.Combine(root, "include")}\" -c \"{genericOutput}\" -o \"{genericObject}\"");
if (!genericGenerated.Contains("setjmp(dnd_eh_frame.environment)", StringComparison.Ordinal) ||
    !genericGenerated.Contains("dnd_exception_matches", StringComparison.Ordinal) ||
    !genericGenerated.Contains("dnd_caught_", StringComparison.Ordinal) ||
    !genericGenerated.Contains("dnd_exception_new", StringComparison.Ordinal))
    throw new Exception("Exception handling lowering missing.");
var literalDefinitions = genericGenerated.Split('\n').Count(line => line.Contains("dnd_string_literal_", StringComparison.Ordinal) && line.Contains("static const struct", StringComparison.Ordinal));
if (literalDefinitions == 0) throw new Exception("Static string literals were not emitted.");
if (genericGenerated.Contains("dnd_string_from_utf8(dnd_value_heap", StringComparison.Ordinal))
    throw new Exception("String literal loading still allocates from the managed heap.");
if (!genericGenerated.Contains("dnd_string_char_at", StringComparison.Ordinal) ||
    !genericGenerated.Contains("dnd_string_starts_with", StringComparison.Ordinal) ||
    !genericGenerated.Contains("dnd_string_ends_with", StringComparison.Ordinal) ||
    !genericGenerated.Contains("dnd_string_index_of", StringComparison.Ordinal) ||
    !genericGenerated.Contains("dnd_string_substring", StringComparison.Ordinal) ||
    !genericGenerated.Contains("dnd_string_concat", StringComparison.Ordinal))
    throw new Exception("Core immutable string lowering missing.");
if (!genericGenerated.Contains("dnd_array_get_length", StringComparison.Ordinal) ||
    !genericGenerated.Contains("dnd_array_get_upper_bound", StringComparison.Ordinal) ||
    !genericGenerated.Contains("dnd_array_clear", StringComparison.Ordinal) ||
    !genericGenerated.Contains("dnd_array_copy", StringComparison.Ordinal) ||
    !genericGenerated.Contains("dnd_array_index_of", StringComparison.Ordinal))
    throw new Exception("System.Array contract lowering missing.");
if (!genericGenerated.Contains("dnd_array_load_scalar", StringComparison.Ordinal) ||
    !genericGenerated.Contains("dnd_array_store_scalar", StringComparison.Ordinal) ||
    !genericGenerated.Contains(", 1u,", StringComparison.Ordinal) ||
    !genericGenerated.Contains(", 2u,", StringComparison.Ordinal) ||
    !genericGenerated.Contains(", 8u,", StringComparison.Ordinal))
    throw new Exception("Primitive scalar array width lowering missing.");
if (!genericGenerated.Contains("Nullable object must have a value.", StringComparison.Ordinal)) throw new Exception("Nullable Value guard missing.");
if (!genericGenerated.Contains("DND_EXCEPTION_INVALID_OPERATION", StringComparison.Ordinal)) throw new Exception("Nullable exception category missing.");
if (!genericGenerated.Contains("DND_TYPE_INT64", StringComparison.Ordinal)) throw new Exception("Wide nullable boxing missing.");
if (!genericGenerated.Contains("dnd_managed_array_at", StringComparison.Ordinal)) throw new Exception("ArraySegment indexer lowering missing.");
if (!genericGenerated.Contains("(DndObject**)(l", StringComparison.Ordinal)) throw new Exception("Embedded generic value GC roots missing.");
if (!genericGenerated.Contains("uint8_t enum_", StringComparison.Ordinal) ||
    !genericGenerated.Contains("int32_t *cur=", StringComparison.Ordinal))
    throw new Exception("Concrete ArraySegment<T> struct enumerator lowering missing.");
if (!genericGenerated.Contains("dnd_interface_resolve", StringComparison.Ordinal))
    throw new Exception("Interface enumeration dispatch lowering missing.");
if (!genericGenerated.Contains("dnd_type_System_Collections_Generic_IEnumerable_1", StringComparison.Ordinal) ||
    !genericGenerated.Contains("dnd_type_System_Collections_Generic_IEnumerator_1", StringComparison.Ordinal) ||
    !genericGenerated.Contains("dnd_type_System_Collections_IEnumerable", StringComparison.Ordinal) ||
    !genericGenerated.Contains("dnd_type_System_Collections_IEnumerator", StringComparison.Ordinal))
    throw new Exception("Generic/non-generic enumeration interface metadata missing.");
if (!genericGenerated.Contains("dnd_type_System_Collections_Generic_ICollection_1", StringComparison.Ordinal) ||
    !genericGenerated.Contains("dnd_type_System_Collections_Generic_IList_1", StringComparison.Ordinal) ||
    !genericGenerated.Contains("dnd_type_System_Collections_Generic_IReadOnlyCollection_1", StringComparison.Ordinal) ||
    !genericGenerated.Contains("dnd_type_System_Collections_Generic_IReadOnlyList_1", StringComparison.Ordinal))
    throw new Exception("Generic collection interface metadata missing.");
if (!genericGenerated.Contains("CompactList_1_g_p_Insert", StringComparison.Ordinal) ||
    !genericGenerated.Contains("CompactList_1_g_p_RemoveAt", StringComparison.Ordinal) ||
    !genericGenerated.Contains("CompactList_1_g_p_CopyTo", StringComparison.Ordinal))
    throw new Exception("Compact collection mutation lowering missing.");
if (genericGenerated.Contains("dnd_type__generic", StringComparison.Ordinal))
    throw new Exception("Generic array emitted unresolved runtime type metadata.");
var concreteEnumerationStart = genericGenerated.IndexOf("Program_TestConcreteEnumeration", StringComparison.Ordinal);
var genericInterfaceStart = genericGenerated.IndexOf("Program_TestGenericInterfaceEnumeration", StringComparison.Ordinal);
if (concreteEnumerationStart < 0 || genericInterfaceStart <= concreteEnumerationStart)
    throw new Exception("Enumeration test bodies missing from AOT output.");
var concreteEnumerationBody = genericGenerated[concreteEnumerationStart..genericInterfaceStart];
if (concreteEnumerationBody.Contains("dnd_object_new", StringComparison.Ordinal) ||
    concreteEnumerationBody.Contains("dnd_box_", StringComparison.Ordinal))
    throw new Exception("Concrete foreach unexpectedly allocates or boxes.");
var sharedDefinitions = genericGenerated.Split('\n')
    .Count(line => line.Contains("Shared_1_g_", StringComparison.Ordinal) && line.Contains("_Marker_", StringComparison.Ordinal) && line.TrimEnd().EndsWith("{", StringComparison.Ordinal));
if (sharedDefinitions != 2)
    throw new Exception($"Expected scalar and reference-shared generic method bodies, found {sharedDefinitions}.");
var identityDefinitions = genericGenerated.Split('\n')
    .Count(line => line.Contains("Program_Identity", StringComparison.Ordinal) && line.TrimEnd().EndsWith("{", StringComparison.Ordinal));
if (identityDefinitions != 4)
    throw new Exception($"Expected type-aware Identity specializations, found {identityDefinitions}.");
if (!genericGenerated.Split('\n').Any(line => line.Contains("Program_Identity", StringComparison.Ordinal) && line.Contains("int64_t", StringComparison.Ordinal)))
    throw new Exception("Wide generic specialization did not emit a 64-bit ABI.");
File.Delete(genericObject);
File.Delete(genericOutput);
Console.WriteLine("DolphinDotNet compiler integration test passed.");

static void Run(string file, string args)
{
    args = CompilerArguments(file, args);
    using var p = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false })
        ?? throw new Exception($"Could not start {file}.");
    p.WaitForExit();
    if (p.ExitCode != 0) throw new Exception($"{file} {args} failed with exit code {p.ExitCode}.");
}

static void RunFailure(string file, string args)
{
    args = CompilerArguments(file, args);
    using var process = Process.Start(new ProcessStartInfo(file, args)
    {
        UseShellExecute = false, RedirectStandardError = true
    }) ?? throw new Exception($"Could not start {file}.");
    var stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode == 0)
        throw new Exception($"Expected invalid debugger arguments to be rejected: {file} {args}. {stderr}");
}

static void AssertPdbRejected(string dll, string directory)
{
    var compiler = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
        "../../../../../tools/DolphinDotNet.Compiler/DolphinDotNet.Compiler.csproj"));
    var arguments = $"run --project \"{compiler}\" -- --bundle \"{dll}\" \"{directory}\" " +
        "--gdb-tcp 192.0.2.10 --gdb-netmask 255.255.255.0 --gdb-gateway 192.0.2.1";
    using var process = Process.Start(new ProcessStartInfo("dotnet", CompilerArguments("dotnet", arguments))
    {
        UseShellExecute = false, RedirectStandardError = true
    }) ?? throw new Exception("Failed to launch debug PDB validation.");
    var diagnostics = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode == 0 || !diagnostics.Contains("No matching Portable PDB", StringComparison.Ordinal))
        throw new Exception("Missing/stale PDB was accepted: " + diagnostics);
}

// CI already built this exact compiler. Preserve standalone test behavior when
// no shared executable is supplied, and keep build/SDK tests on MSBuild.
static string CompilerArguments(string file, string arguments)
{
    var dll = Environment.GetEnvironmentVariable("DND_COMPILER_DLL");
    if (file != "dotnet" || string.IsNullOrEmpty(dll)) return arguments;
    var project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
        "../../../../../tools/DolphinDotNet.Compiler/DolphinDotNet.Compiler.csproj"));
    var prefix = $"run --project \"{project}\" -- ";
    if (!arguments.StartsWith(prefix, StringComparison.Ordinal)) return arguments;
    if (!File.Exists(dll)) throw new FileNotFoundException("Shared CI compiler is missing.", dll);
    return $"\"{dll}\" " + arguments[prefix.Length..];
}
