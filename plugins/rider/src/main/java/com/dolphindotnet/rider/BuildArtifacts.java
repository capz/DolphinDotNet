package com.dolphindotnet.rider;

import java.io.IOException;
import java.nio.file.*;
import java.util.*;

public record BuildArtifacts(Path elf, Path sourceMap, Path addressMap) {
    public static BuildArtifacts discover(Path folder) throws IOException {
        if (!Files.isDirectory(folder)) throw new IOException("Choose an existing debug build folder.");
        List<Path> elfs;
        try (var files = Files.list(folder)) {
            elfs = files.filter(p -> Files.isRegularFile(p) && p.getFileName().toString().endsWith(".elf")).sorted().toList();
        }
        if (elfs.size() != 1) throw new IOException("The debug build folder must contain exactly one ELF file.");
        var elf = elfs.getFirst();
        var name = elf.getFileName().toString();
        var stem = name.substring(0, name.length() - 4);
        // MSBuild publishes <Assembly>.dnd-debug.json; native bundles retain dnd-il-debug.json.
        var publishedSource = folder.resolve(stem + ".dnd-debug.json");
        var source = Files.isRegularFile(publishedSource) ? publishedSource : folder.resolve("dnd-il-debug.json");
        var addresses = folder.resolve(stem + ".dnd-addresses.json");
        if (!Files.isRegularFile(source) || !Files.isRegularFile(addresses))
            throw new IOException("The debug build needs a source map (<Assembly>.dnd-debug.json or dnd-il-debug.json) and " + addresses.getFileName() + ".");
        return new BuildArtifacts(elf.toAbsolutePath(), source.toAbsolutePath(), addresses.toAbsolutePath());
    }
    public Map<String, Object> attachArguments() {
        return Map.of("elf", elf.toString(), "sourceMap", sourceMap.toString(), "addressMap", addressMap.toString());
    }
}
