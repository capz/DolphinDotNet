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
if (identityDefinitions != 3)
    throw new Exception($"Expected reference-shared Identity plus scalar and wide-value specializations, found {identityDefinitions}.");
if (!genericGenerated.Split('\n').Any(line => line.Contains("Program_Identity", StringComparison.Ordinal) && line.Contains("int64_t", StringComparison.Ordinal)))
    throw new Exception("Wide generic specialization did not emit a 64-bit ABI.");
File.Delete(genericObject);
File.Delete(genericOutput);
Console.WriteLine("DolphinDotNet compiler integration test passed.");

static void Run(string file, string args)
{
    using var p = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false })
        ?? throw new Exception($"Could not start {file}.");
    p.WaitForExit();
    if (p.ExitCode != 0) throw new Exception($"{file} {args} failed with exit code {p.ExitCode}.");
}
