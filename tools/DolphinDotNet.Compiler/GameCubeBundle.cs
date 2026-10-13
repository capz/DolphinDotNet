using System.Text;

namespace DolphinDotNet.Compiler;

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
        ("graphics", new[] {"Dolphin.Graphics.", "Dolphin.Models."}, new[] {"-lopengx", "-logc", "-lm"}),
        ("input", new[] {"Dolphin.Input."}, new[] {"-logc"}),
        ("sound", new[] {"System.Media.", "Dolphin.Audio.", "Dolphin.Sound."}, Array.Empty<string>())
    };

    internal static void Emit(string assembly, string directory, AotCompilation compilation, string platform)
    {
        var root = FindRoot();
        var reachable = compilation.ValueMethods.Select(x => x.Key.TypeName)
            .Distinct(StringComparer.Ordinal).ToArray();
        var features = FeatureMap.Where(feature =>
            reachable.Any(type => feature.Prefixes.Any(prefix =>
                type.StartsWith(prefix, StringComparison.Ordinal)))).ToArray();
        if (features.Any(x => x.Name == "sound"))
            throw new NotSupportedException("Native sound backend not implemented; cannot determine a safe audio link set.");

        // libogc2 native modules are currently GameCube-specific. Do not
        // silently compile those files as Wii/Wii U implementations.
        if (platform == "wiiu" && features.Any(x =>
            x.Name is "networking" or "storage" or "graphics" or "input" or "aram"))
            throw new NotSupportedException("Wii U target requires wut-native adapters for I/O, graphics and input. GameCube libogc2 code cannot be linked into an RPX.");

        Directory.CreateDirectory(directory);
        foreach (var sub in new[] { "source", "include", "scripts" })
            Directory.CreateDirectory(Path.Combine(directory, sub));

        foreach (var path in Directory.GetFiles(Path.Combine(root, "include"), "*.h"))
            File.Copy(path, Path.Combine(directory, "include", Path.GetFileName(path)), true);
        File.Copy(Path.Combine(root, "scripts", "dol_sections.py"),
            Path.Combine(directory, "scripts", "dol_sections.py"), true);

        var entry = compilation.ValueMethods.FirstOrDefault(m => m.Key.Name == "Main")
            ?? throw new InvalidDataException("AOT entry point missing.");
        File.WriteAllText(Path.Combine(directory, "source", "generated_program.c"),
            ValueCBackend.EmitProgram(compilation.ValueMethods, entry.Key, compilation.Model, compilation.Graph));

        var sources = new[] {
            "aram", "bcl", "card_transaction", "console_overlay", "fst",
            "fst_gamecube", "gc_stw", "graphics_opengx", "input_gamecube",
            "main", "managed_runtime", "model", "model_animation",
            "model_graphics", "model_transform", "network", "network_gamecube",
            "network_gamecube_backend", "performance", "platform_gamecube",
            "storage", "storage_gamecube", "stw", "system", "text", "runtime", "vm", "worker"
        };

        if (platform == "wiiu")
        {
            // This subset is independent of libogc. A Wii U-native entry
            // and WUT backend are required; never embed GameCube main.c.
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

        File.WriteAllText(Path.Combine(directory, "Makefile"), Makefile(platform));
        var report = new StringBuilder();
        report.AppendLine("DolphinDotNet AOT Native Dependencies");
        report.AppendLine("Target: " + platform);
        report.AppendLine("Assembly: " + Path.GetFileName(assembly));
        report.AppendLine("Detection: declaring types of reachable AOT methods; unused using directives are ignored.");
        report.AppendLine("Selected: " + (features.Length == 0 ? "core" : string.Join(", ", features.Select(f => f.Name))));
        report.AppendLine("Feature -> namespace prefixes -> native libraries:");
        foreach (var feature in FeatureMap)
            report.AppendLine(feature.Name + " -> " + string.Join(", ", feature.Prefixes) +
                " -> " + (feature.Libs.Length > 0 ? string.Join(" ", feature.Libs) : "UNSUPPORTED"));
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

    private static string Makefile(string platform)
    {
        var isWiiU = platform == "wiiu";
        var rules = isWiiU ? "$(DEVKITPRO)/wut/share/wut_rules"
            : "$(DEVKITPRO)/libogc2/" + (platform == "wii" ? "wii_rules" : "gamecube_rules");
        var suffix = isWiiU ? "rpx" : "dol";
        var libs = isWiiU ? "-lwut -lm" : "-lopengx -lfat -ldvm -liso9660 -lbba -logc -lm";
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
        builder.AppendLine("CFLAGS := -O2 -g -Wall -Wextra $(MACHDEP) $(INCLUDE)" +
            (isWiiU ? " -D__WIIU__ -D__WUT__" : " -DDND_GAMECUBE_STORAGE"));
        builder.AppendLine("LDFLAGS := -g $(MACHDEP) -Wl,-Map,$(notdir $@).map");
        builder.AppendLine("ifneq ($(BUILD),$(notdir $(CURDIR)))");
        builder.AppendLine("export OUTPUT := $(CURDIR)/$(TARGET)");
        builder.AppendLine("export VPATH := $(CURDIR)/source");
        builder.AppendLine("export DEPSDIR := $(CURDIR)/$(BUILD)");
        builder.AppendLine("export OFILES := $(notdir $(patsubst %.c,%.o,$(wildcard source/*.c)))");
        builder.AppendLine("export INCLUDE := -I$(CURDIR)/include -I" + library + "/include -I$(CURDIR)/build");
        builder.AppendLine("export LIBPATHS := -L" + library + "/lib");
        builder.AppendLine("export DOL_VALIDATOR := $(CURDIR)/scripts/dol_sections.py");
        builder.AppendLine(".PHONY: all clean");
        builder.AppendLine("all: $(BUILD)");
        builder.AppendLine("\t@$(MAKE) --no-print-directory -C $(BUILD) -f $(CURDIR)/Makefile");
        builder.AppendLine("$(BUILD):");
        builder.AppendLine("\t@mkdir -p $@");
        builder.AppendLine("clean:");
        builder.AppendLine("\t@rm -rf $(BUILD) $(OUTPUT).elf $(OUTPUT)." + suffix);
        builder.AppendLine("else");
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
        }
        builder.AppendLine("$(OUTPUT).elf: $(OFILES)");
        builder.AppendLine("\t$(CC) $^ $(LDFLAGS) $(LIBPATHS) $(LIBS) -o $@");
        builder.AppendLine("-include $(DEPENDS)");
        builder.AppendLine("endif");
        return builder.ToString();
    }
}
