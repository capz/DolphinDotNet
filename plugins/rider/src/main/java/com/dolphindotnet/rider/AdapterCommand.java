package com.dolphindotnet.rider;

import com.intellij.openapi.application.PathManager;
import com.intellij.execution.configurations.GeneralCommandLine;
import java.nio.file.*;

final class AdapterCommand {
    static GeneralCommandLine create(String... arguments) {
        // The distribution places this JAR in <plugin>/lib and the adapter in
        // <plugin>/adapter. Use the public class-location API, not the internal
        // plugin registry, so this also follows custom IDE plugin directories.
        var jar = PathManager.getJarForClass(AdapterCommand.class);
        if (jar == null || !Files.isRegularFile(jar) || jar.getParent() == null || jar.getParent().getParent() == null)
            throw new IllegalStateException("DolphinDotNet plugin JAR is unavailable.");
        Path adapter = jar.getParent().getParent().resolve("adapter/DolphinDotNet.DebugAdapter.dll");
        if (!Files.isRegularFile(adapter)) throw new IllegalStateException("The plugin package is missing its debug adapter.");
        return new GeneralCommandLine("dotnet", adapter.toString()).withParameters(arguments);
    }
}
