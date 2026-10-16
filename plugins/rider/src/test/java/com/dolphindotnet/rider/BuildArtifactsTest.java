package com.dolphindotnet.rider;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;
import java.nio.file.*;
import java.io.IOException;
import static org.junit.jupiter.api.Assertions.*;

class BuildArtifactsTest {
    @TempDir Path folder;
    @Test void requireOneElfAndMatchingSidecars() throws Exception {
        assertThrows(IOException.class, () -> BuildArtifacts.discover(folder));
        Files.writeString(folder.resolve("Game.elf"), "elf");
        assertThrows(IOException.class, () -> BuildArtifacts.discover(folder));
        Files.writeString(folder.resolve("dnd-il-debug.json"), "source");
        Files.writeString(folder.resolve("Game.dnd-addresses.json"), "addresses");
        var result = BuildArtifacts.discover(folder); assertEquals(folder.resolve("Game.elf"), result.elf());
        assertEquals(3, result.attachArguments().size());
        Files.delete(folder.resolve("dnd-il-debug.json"));
        Files.writeString(folder.resolve("Game.dnd-debug.json"), "published source");
        assertEquals("Game.dnd-debug.json", BuildArtifacts.discover(folder).sourceMap().getFileName().toString());
        Files.writeString(folder.resolve("Other.elf"), "elf");
        assertThrows(IOException.class, () -> BuildArtifacts.discover(folder));
    }
}
