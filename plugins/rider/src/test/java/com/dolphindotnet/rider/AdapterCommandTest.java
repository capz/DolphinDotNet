package com.dolphindotnet.rider;

import com.intellij.execution.configurations.GeneralCommandLine;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;
import java.lang.reflect.InvocationTargetException;
import java.net.*;
import java.nio.file.*;
import java.util.*;
import java.util.jar.*;
import static org.junit.jupiter.api.Assertions.*;

class AdapterCommandTest {
    @TempDir Path temp;
    @Test void findsAdapterBesidePackagedJarAndReportsMissingPayload() throws Exception {
        var root = temp.resolve("plugin with spaces 日本");
        var lib = Files.createDirectories(root.resolve("lib"));
        var jar = lib.resolve("plugin.jar");
        var name = AdapterCommand.class.getName();
        var resource = name.replace('.', '/') + ".class";
        try (var output = new JarOutputStream(Files.newOutputStream(jar));
             var input = Objects.requireNonNull(AdapterCommand.class.getResourceAsStream("/" + resource))) {
            output.putNextEntry(new JarEntry(resource)); input.transferTo(output); output.closeEntry();
        }
        // Load this one class/resource from its distribution JAR, keeping the
        // pinned IDE's public API classes in the parent loader.
        try (var loader = new URLClassLoader(new URL[]{jar.toUri().toURL()}, AdapterCommand.class.getClassLoader()) {
            @Override protected Class<?> loadClass(String className, boolean resolve) throws ClassNotFoundException {
                if (!className.equals(name)) return super.loadClass(className, resolve);
                var type = findLoadedClass(className);
                if (type == null) type = findClass(className);
                if (resolve) resolveClass(type);
                return type;
            }
            @Override public URL getResource(String path) { return path.equals(resource) ? findResource(path) : super.getResource(path); }
        }) {
            var create = loader.loadClass(name).getDeclaredMethod("create", String[].class);
            create.setAccessible(true);
            var payload = Files.createDirectories(root.resolve("adapter")).resolve("DolphinDotNet.DebugAdapter.dll");
            Files.writeString(payload, "fixture");
            var command = (GeneralCommandLine) create.invoke(null, (Object) new String[]{"--list-serial-ports"});
            assertEquals("dotnet", command.getExePath());
            assertEquals(List.of(payload.toString(), "--list-serial-ports"), command.getParametersList().getList());
            Files.delete(payload);
            var error = assertThrows(InvocationTargetException.class, () -> create.invoke(null, (Object) new String[0]));
            assertInstanceOf(IllegalStateException.class, error.getCause());
            assertTrue(error.getCause().getMessage().contains("missing its debug adapter"));
        }
    }
}
