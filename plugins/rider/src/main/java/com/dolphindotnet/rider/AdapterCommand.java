package com.dolphindotnet.rider;

import com.intellij.ide.plugins.PluginManagerCore;
import com.intellij.openapi.extensions.PluginId;
import com.intellij.execution.configurations.GeneralCommandLine;
import java.nio.file.*;

final class AdapterCommand {
    static GeneralCommandLine create(String... arguments) {
        var descriptor = PluginManagerCore.getPlugin(PluginId.getId("com.dolphindotnet.rider"));
        if (descriptor == null) throw new IllegalStateException("DolphinDotNet plugin is unavailable.");
        Path adapter = descriptor.getPluginPath().resolve("adapter/DolphinDotNet.DebugAdapter.dll");
        if (!Files.isRegularFile(adapter)) throw new IllegalStateException("The plugin package is missing its debug adapter.");
        return new GeneralCommandLine("dotnet", adapter.toString()).withParameters(arguments);
    }
}
