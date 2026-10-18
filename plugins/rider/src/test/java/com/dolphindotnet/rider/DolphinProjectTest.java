package com.dolphindotnet.rider;

import org.junit.jupiter.api.Test;
import java.nio.file.Files;
import java.nio.file.Path;
import static org.junit.jupiter.api.Assertions.*;

class DolphinProjectTest {
    @Test void recognizesSdkAttributesAndVersionedSdkElements() {
        assertTrue(DolphinProject.usesSdk("<Project Sdk='DolphinDotNet.Sdk'/>"));
        assertTrue(DolphinProject.usesSdk("<Project Sdk='Other; DolphinDotNet.Sdk/1.0'/>"));
        assertTrue(DolphinProject.usesSdk("<Project><Sdk Name='DolphinDotNet.Sdk' Version='1.0'/></Project>"));
    }
    @Test void recognizesRepositoryImportsOnBothPlatforms() {
        assertTrue(DolphinProject.usesSdk("<Project><Import Project='../../sdk/DolphinDotNet.Sdk/Sdk/Sdk.props'/></Project>"));
        assertTrue(DolphinProject.usesSdk("<Project><Import Project='..\\..\\sdk\\DolphinDotNet.Sdk\\Sdk\\Sdk.targets'/></Project>"));
    }
    @Test void recognizesActualSamplesButNotTheDesktopLibraries() throws Exception {
        var root = Path.of(System.getProperty("dnd.repositoryRoot"));
        int samples = 0;
        try (var directories = Files.list(root.resolve("samples"))) {
            for (var directory : directories.filter(Files::isDirectory).toList()) {
                var project = directory.resolve(directory.getFileName() + ".csproj");
                if (Files.exists(project)) {
                    assertTrue(DolphinProject.usesSdk(Files.readString(project)), project.toString()); samples++;
                }
            }
        }
        assertTrue(samples > 0);
        assertFalse(DolphinProject.usesSdk(Files.readString(root.resolve("src/DolphinDotNet.Core/DolphinDotNet.Core.csproj"))));
    }
    @Test void leavesRegularDotnetProjectsAndCommentsAlone() {
        assertFalse(DolphinProject.usesSdk("<Project Sdk='Microsoft.NET.Sdk'/>"));
        assertFalse(DolphinProject.usesSdk("<Project><!-- <Sdk Name='DolphinDotNet.Sdk'/> --></Project>"));
        assertFalse(DolphinProject.usesSdk("<Project><PropertyGroup><Note>DolphinDotNet.Sdk</Note></PropertyGroup></Project>"));
        assertFalse(DolphinProject.usesSdk("not XML"));
    }
}
