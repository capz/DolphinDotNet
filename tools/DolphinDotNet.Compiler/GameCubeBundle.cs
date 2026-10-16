using System.Text;
using System.Text.Json;

namespace DolphinDotNet.Compiler;

// TCP debugging uses libogc2's separate uIP/BBA stack. Keep addresses on the
// host build configuration rather than silently embedding a LAN-specific IP.
internal sealed record GdbTcpOptions(string Ip, string Netmask, string Gateway, int Port)
{
    internal static GdbTcpOptions Parse(string ip, string netmask, string gateway, string port)
    {
        static string Address(string value, string name)
        {
            if (!System.Net.IPAddress.TryParse(value, out var address) ||
                address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
                value != address.ToString())
                throw new ArgumentException(name + " must be a dotted-decimal IPv4 address.");
            return address.ToString();
        }
        if (!int.TryParse(port, out int parsedPort) || parsedPort is < 1 or > 65535)
            throw new ArgumentException("GDB TCP port must be between 1 and 65535.");
        return new GdbTcpOptions(Address(ip, "GDB IP"), Address(netmask, "GDB netmask"),
            Address(gateway, "GDB gateway"), parsedPort);
    }
}

/// <summary>
/// Produces a relocatable native build directory using the reachable AOT
/// method graph. Native libraries are conservative until the monolithic
/// runtime has per-feature link boundaries.
/// </summary>
internal static class GameCubeBundle
{
    private static readonly (string Name, string[] Prefixes, string[] Libs)[] FeatureMap =
    {
        ("networking", new[] {"System.Net.", "Dolphin.Network."}, new[] {"-lbba", "-logc"}),
        ("storage", new[] {"System.IO.", "Dolphin.Storage."}, new[] {"-lfat", "-ldvm", "-liso9660", "-logc"}),
        ("aram", new[] {"Dolphin.IO.Aram", "Dolphin.Aram."}, new[] {"-logc"}),
        ("graphics", new[] {"Dolphin.Graphics.", "Dolphin.Models."}, new[] {"-logc", "-lm"}),
        ("input", new[] {"Dolphin.Input."}, new[] {"-logc"}),
        ("timing", new[] {"Dolphin.Timing."}, new[] {"-logc"}),
        ("audio", new[] {"Dolphin.Audio."}, new[] {"-laesnd", "-logc"}),
        ("unsupported-sound", new[] {"System.Media.", "Dolphin.Sound."}, Array.Empty<string>())
    };

    internal static void Emit(string assembly, string directory, AotCompilation compilation, string platform, GdbTcpOptions? gdbTcp = null, bool debugSymbols = false)
    {
        if (gdbTcp is not null && platform != "gamecube")
            throw new NotSupportedException("The libogc2 GDB TCP stub is supported only for GameCube builds.");
        debugSymbols |= gdbTcp is not null;
        if (debugSymbols && platform != "gamecube")
            throw new NotSupportedException("Native C# source mapping requires the GameCube target.");
        var root = FindRoot();
        var reachable = compilation.ValueMethods.Select(x => x.Key.TypeName)
            .Distinct(StringComparer.Ordinal).ToArray();
        // Managed API compatibility is cumulative, native binary linkage is not.
        var supportedApiPlatforms = platform switch
        {
            "gamecube" => new[] { "gamecube" },
            "wii" => new[] { "gamecube", "wii" },
            "wiiu" => new[] { "gamecube", "wii", "wiiu" },
            _ => throw new ArgumentOutOfRangeException(nameof(platform))
        };
        // Reject reachable platform-exclusive APIs; base Dolphin/System APIs
        // remain available on every target through target-specific adapters.
        foreach (var type in reachable)
        {
            var required = type.StartsWith("Dolphin.WiiU.", StringComparison.Ordinal) ||
                           type.StartsWith("DolphinDotNet.WiiU.", StringComparison.Ordinal)
                ? "wiiu"
                : type.StartsWith("Dolphin.Wii.", StringComparison.Ordinal) ||
                  type.StartsWith("DolphinDotNet.Wii.", StringComparison.Ordinal)
                    ? "wii" : "gamecube";
            if (!supportedApiPlatforms.Contains(required, StringComparer.Ordinal))
                throw new NotSupportedException(
                    $"API '{type}' requires platform '{required}', but target is '{platform}'.");
        }
        var features = FeatureMap.Where(feature =>
            reachable.Any(type => feature.Prefixes.Any(prefix =>
                type.StartsWith(prefix, StringComparison.Ordinal)))).ToArray();
        if (features.Any(x => x.Name == "unsupported-sound"))
            throw new NotSupportedException("Use Dolphin.Audio for native audio playback; System.Media and Dolphin.Sound have no native backend.");

        // libogc2 native modules are currently GameCube-specific. Do not
        // silently compile those files as Wii/Wii U implementations.
        if (platform == "wiiu" && features.Any(x =>
            x.Name is "networking" or "storage" or "graphics" or "input" or "aram" or "audio"))
            throw new NotSupportedException("Wii U target requires wut-native adapters for I/O, graphics and input. GameCube libogc2 code cannot be linked into an RPX.");

        Directory.CreateDirectory(directory);
        foreach (var sub in new[] { "source", "include", "scripts" })
            Directory.CreateDirectory(Path.Combine(directory, sub));

        foreach (var path in Directory.GetFiles(Path.Combine(root, "include"), "*.h"))
            File.Copy(path, Path.Combine(directory, "include", Path.GetFileName(path)), true);
        foreach (var path in Directory.GetFiles(Path.Combine(root, "source"), "*.inc"))
            File.Copy(path, Path.Combine(directory, "source", Path.GetFileName(path)), true);
        Directory.CreateDirectory(Path.Combine(directory,"vendor","ufbx"));
        File.Copy(Path.Combine(root,"vendor","ufbx","LICENSE"),Path.Combine(directory,"vendor","ufbx","LICENSE"),true);
        File.Copy(Path.Combine(root, "scripts", "dol_sections.py"),
            Path.Combine(directory, "scripts", "dol_sections.py"), true);
        if (platform == "gamecube")
        {
            // Standalone DOLs require the same verified EXI correction as the
            // repository's primary GameCube Makefile; stock libogc2 hangs in
            // EXI_Sync in Dolphin's default hardware configuration.
            Directory.CreateDirectory(Path.Combine(directory, "vendor", "libogc2"));
            foreach (var file in new[] { "build_exi_overlay.sh", "verify_exi_source.py" })
                File.Copy(Path.Combine(root, "scripts", file),
                    Path.Combine(directory, "scripts", file), true);
            File.Copy(Path.Combine(root, "vendor", "libogc2", "exi.c"),
                Path.Combine(directory, "vendor", "libogc2", "exi.c"), true);
        }

        var entry = compilation.ValueMethods.Single(m => m.Key == compilation.Entry);
        var sourceMap = debugSymbols ? PortablePdbSourceMap.Load(compilation) : null;
        File.WriteAllText(Path.Combine(directory, "source", "generated_program.c"),
            ValueCBackend.EmitProgram(compilation.ValueMethods, entry.Key, compilation.Model,
                compilation.Graph, sourceMap?.Methods));
        if (debugSymbols)
        {
            File.Copy(Path.Combine(root, "scripts", "dnd_debug_addresses.py"),
                Path.Combine(directory, "scripts", "dnd_debug_addresses.py"), true);
            // Portable PDB identity must match the exact assemblies being AOT compiled.
            // Source points are host-only; an ELF/native address map comes next.
            var methods = compilation.ValueMethods
                .Select(method => new
                {
                    symbol = ValueCBackend.Symbol(method.Key),
                    assembly = method.Key.AssemblyName,
                    declaringType = method.Key.TypeName,
                    name = method.Key.Name,
                    signature = method.Key.Signature,
                    methodToken = System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(
                        compilation.Model.Methods[method.Key].Handle),
                    sourceLocations = sourceMap.Methods[method.Key]
                        .OrderBy(location => location.Key)
                        .Select(location => new
                        {
                            ilOffset = location.Key,
                            sequencePointIlOffset = location.Value.SequencePointIlOffset,
                            document = location.Value.Document,
                            startLine = location.Value.StartLine,
                            startColumn = location.Value.StartColumn,
                            endLine = location.Value.EndLine,
                            endColumn = location.Value.EndColumn
                        }).ToArray(),
                    ilOffsets = method.Blocks.SelectMany(block => block.Instructions)
                        .Select(instruction => instruction.CilOffset)
                        .Concat(method.Blocks.Select(block => block.Terminator?.CilOffset ?? -1))
                        .Where(offset => offset >= 0).Distinct().OrderBy(offset => offset).ToArray()
                })
                .OrderBy(method => method.symbol, StringComparer.Ordinal).ToArray();
            File.WriteAllText(Path.Combine(directory, "dnd-il-debug.json"),
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 2,
                    assemblies = sourceMap.Assemblies
                        .Select(identity => new { name = identity.Name,
                            moduleVersionId = identity.ModuleVersionId, pdbId = identity.PdbId }),
                    methods
                },
                    new JsonSerializerOptions { WriteIndented = true }));
        }

        if (gdbTcp is not null)
        {
            // A debug-symbols-only emulator build must not link libdb or trap at startup.
            File.WriteAllText(Path.Combine(directory, "source", "debug_gamecube.c"),
                "#include <debug.h>\n" +
                $"const char *tcp_localip = \"{gdbTcp.Ip}\";\n" +
                $"const char *tcp_netmask = \"{gdbTcp.Netmask}\";\n" +
                $"const char *tcp_gateway = \"{gdbTcp.Gateway}\";\n" +
                "void dnd_gdb_init(void) {\n" +
                $"    DEBUG_Init(GDBSTUB_DEVICE_TCP, {gdbTcp.Port});\n" +
                "    _break(); /* Wait at a deterministic startup rendezvous. */\n" +
                "}\n");
        }

        var sources = new[] {
            "aram", "audio", "audio_gamecube", "async_runtime", "bcl", "card_transaction", "console_overlay", "fst",
            "fst_gamecube", "iso_file", "hardware", "gc_stw", "graphics_gx", "graphics_device", "draw_resources", "gx_raw", "gx_guard", "input", "input_gamecube",
            "main", "managed_runtime", "model", "model_obj", "model_source", "model_ufbx", "model_animation",
            "model_graphics", "model_transform", "network", "network_gamecube",
            "network_gamecube_backend", "performance", "platform_gamecube",
            "storage", "storage_gamecube", "stw", "system", "text", "timing", "worker"
        };

        if (platform == "wiiu")
        {
            // This subset is independent of libogc. A Wii U-native entry
            // and WUT backend are required; never embed GameCube main.c.
            if(reachable.Any(t=>t.StartsWith("Dolphin.Threading.Tasks.",StringComparison.Ordinal)))
                throw new NotSupportedException("Automatic async dispatch requires a target-specific thread backend; Wii U is not implemented.");
            sources = new[] { "bcl", "managed_runtime", "text", "system" };
            File.WriteAllText(Path.Combine(directory, "source", "wiiu_main.c"),
                "#include \"dnd_managed.h\"\n#include <stdlib.h>\n" +
                "extern intptr_t dnd_aot_entry(DndManagedHeap *heap);\n" +
                "static unsigned char gc_memory[256 * 1024];\n" +
                "int main(void) { DndManagedHeap heap; dnd_managed_heap_init(&heap, gc_memory, sizeof(gc_memory)); " +
                "return (int)dnd_aot_entry(&heap); }\n");
        }
        foreach (var name in sources)
            File.Copy(Path.Combine(root, "source", name + ".c"),
                Path.Combine(directory, "source", name + ".c"), true);

        File.WriteAllText(Path.Combine(directory, "Makefile"), Makefile(platform, features.Any(f => f.Name == "networking"), gdbTcp is not null, debugSymbols));
        var report = new StringBuilder();
        report.AppendLine("DolphinDotNet AOT Native Dependencies");
        report.AppendLine("Target: " + platform);
        report.AppendLine("Assembly: " + Path.GetFileName(assembly));
        report.AppendLine("Managed source symbols: " + (debugSymbols ? "enabled" : "disabled"));
        report.AppendLine("GDB TCP stub: " + (gdbTcp is null ? "disabled" : $"enabled on {gdbTcp.Ip}:{gdbTcp.Port}"));
        if (gdbTcp is not null)
            report.AppendLine("Normal automatic BBA initialization is disabled; the GDB stub owns a separate polling network stack. Concurrent application networking is not validated.");
        report.AppendLine("Allowed managed API platforms: " + string.Join(", ", supportedApiPlatforms));
        report.AppendLine("Managed API compatibility does not imply native binary/library compatibility.");
        report.AppendLine("Detection: declaring types of reachable AOT methods; unused using directives are ignored.");
        report.AppendLine("Selected: " + (features.Length == 0 ? "core" : string.Join(", ", features.Select(f => f.Name))));
        report.AppendLine("Feature -> namespace prefixes -> native libraries:");
        foreach (var feature in FeatureMap)
            report.AppendLine(feature.Name + " -> " + string.Join(", ", feature.Prefixes) +
                " -> " + (feature.Libs.Length == 0 ? "UNSUPPORTED" :
                    platform == "wiiu" ? "WUT-NATIVE BACKEND REQUIRED (not libogc)" :
                    string.Join(" ", feature.Libs)));
        report.AppendLine("GameCube and Wii use separate libogc2 target library directories and rules.");
        report.AppendLine("Wii U uses wut and RPX, not ELF-to-DOL conversion.");
        report.AppendLine("Library pruning is not yet safe because the existing native runtime is monolithic.");
        report.AppendLine("Wii and Wii U native backends are incomplete; validate or provide target-specific adapters before deploying.");
        File.WriteAllText(Path.Combine(directory, "DEPENDENCIES.txt"), report.ToString());
    }

    private static string FindRoot()
    {
        var packaged = Path.Combine(AppContext.BaseDirectory, "native");
        if (File.Exists(Path.Combine(packaged, "source", "main.c")) &&
            File.Exists(Path.Combine(packaged, "scripts", "dol_sections.py")))
            return packaged;
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "source", "main.c")) &&
                File.Exists(Path.Combine(dir.FullName, "scripts", "dol_sections.py")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("The DolphinDotNet repository's native sources are required to emit a self-contained build directory.");
    }

    private static string Makefile(string platform, bool usesNetworking, bool gdbTcp, bool debugSymbols)
    {
        var isWiiU = platform == "wiiu";
        var rules = isWiiU ? "$(DEVKITPRO)/wut/share/wut_rules"
            : "$(DEVKITPRO)/libogc2/" + (platform == "wii" ? "wii_rules" : "gamecube_rules");
        var suffix = isWiiU ? "rpx" : "dol";
        var libs = isWiiU ? "-lwut -lm" : (gdbTcp ? "-ldb " : "") + "-lfat -ldvm -liso9660 -lbba -laesnd -logc -lm";
        var library = isWiiU ? "$(DEVKITPRO)/wut"
            : "$(DEVKITPRO)/libogc2/" + (platform == "wii" ? "wii" : "gamecube");
        var builder = new StringBuilder();
        builder.AppendLine("ifeq ($(strip $(DEVKITPRO)),)");
        builder.AppendLine("$(error DEVKITPRO must be set)");
        builder.AppendLine("endif");
        builder.AppendLine("ifeq ($(strip $(DEVKITPPC)),)");
        builder.AppendLine("$(error DEVKITPPC must be set)");
        builder.AppendLine("endif");
        builder.AppendLine("include " + rules);
        builder.AppendLine("TARGET := Game");
        builder.AppendLine("BUILD := build");
        builder.AppendLine("SOURCES := source");
        builder.AppendLine("INCLUDES := include");
        builder.AppendLine("LIBDIRS := " + library);
        builder.AppendLine("LIBS := " + libs);
        var symbols = debugSymbols ? "-Og -g3 -gdwarf-4 -fno-inline -fno-omit-frame-pointer" : "-O2 -g";
        builder.AppendLine("CFLAGS := " + symbols + " -Wall -Wextra $(MACHDEP) $(INCLUDE)" +
            (isWiiU ? " -D__WIIU__ -D__WUT__" :
                gdbTcp ? " -DDND_GAMECUBE_STORAGE -DDND_NO_AUTONETWORK -DDND_GDB_ENABLED" :
                usesNetworking ? " -DDND_GAMECUBE_STORAGE" :
                " -DDND_GAMECUBE_STORAGE -DDND_NO_AUTONETWORK"));
        builder.AppendLine("LDFLAGS := " + (debugSymbols ? "-g3 -gdwarf-4" : "-g") + " $(MACHDEP) -Wl,-Map,$(notdir $@).map");
        builder.AppendLine("ifneq ($(BUILD),$(notdir $(CURDIR)))");
        builder.AppendLine(".DEFAULT_GOAL := all");
        builder.AppendLine("export OUTPUT := $(CURDIR)/$(TARGET)");
        builder.AppendLine("export VPATH := $(CURDIR)/source");
        builder.AppendLine("export DEPSDIR := $(CURDIR)/$(BUILD)");
        builder.AppendLine("export OFILES := $(notdir $(patsubst %.c,%.o,$(wildcard source/*.c)))");
        builder.AppendLine("export INCLUDE := -I$(CURDIR)/include -I" + library + "/include -I$(CURDIR)/build");
        builder.AppendLine("export LIBPATHS := " + (platform == "gamecube" ?
            "-L$(CURDIR)/build/exi-overlay " : "") + "-L" + library + "/lib");
        builder.AppendLine("export DOL_VALIDATOR := $(CURDIR)/scripts/dol_sections.py");
        if (platform == "gamecube")
        {
            builder.AppendLine("EXI_OVERLAY := $(CURDIR)/build/exi-overlay/libogc.a");
            builder.AppendLine("$(EXI_OVERLAY): scripts/build_exi_overlay.sh scripts/verify_exi_source.py vendor/libogc2/exi.c");
            builder.AppendLine("\t@bash scripts/build_exi_overlay.sh");
        }
        builder.AppendLine(".PHONY: all clean");
        builder.AppendLine("all: " + (platform == "gamecube" ?
            "$(EXI_OVERLAY) $(BUILD)" : "$(BUILD)"));
        builder.AppendLine("\t@$(MAKE) --no-print-directory -C $(BUILD) -f $(CURDIR)/Makefile");
        builder.AppendLine("$(BUILD):");
        builder.AppendLine("\t@mkdir -p $@");
        builder.AppendLine("clean:");
        builder.AppendLine("\t@rm -rf $(BUILD) $(OUTPUT).elf $(OUTPUT)." + suffix);
        builder.AppendLine("else");
        builder.AppendLine(".DEFAULT_GOAL := $(OUTPUT)." + suffix);
        builder.AppendLine("DEPENDS := $(OFILES:.o=.d)");
        if (isWiiU)
        {
            builder.AppendLine("$(OUTPUT).rpx: $(OUTPUT).elf");
            builder.AppendLine("\telf2rpl $< $@");
        }
        else
        {
            builder.AppendLine("$(OUTPUT).dol: $(OUTPUT).elf");
            builder.AppendLine("\telf2dol $< $@");
            builder.AppendLine("\tpython3 \"$(DOL_VALIDATOR)\" --pad $@");
            if (debugSymbols)
                builder.AppendLine("\tpython3 \"$(dir $(OUTPUT))scripts/dnd_debug_addresses.py\" --elf $(OUTPUT).elf --source-map \"$(dir $(OUTPUT))dnd-il-debug.json\" --output $(OUTPUT).dnd-addresses.json --readelf \"$(DEVKITPPC)/bin/powerpc-eabi-readelf\"");
        }
        builder.AppendLine("$(OUTPUT).elf: $(OFILES)");
        builder.AppendLine("\t$(CC) $^ $(LDFLAGS) $(LIBPATHS) $(LIBS) -o $@");
        builder.AppendLine("-include $(DEPENDS)");
        builder.AppendLine("endif");
        return builder.ToString();
    }
}
