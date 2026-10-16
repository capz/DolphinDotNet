package com.dolphindotnet.rider;

import com.intellij.execution.*;
import com.intellij.execution.configurations.*;
import com.intellij.execution.runners.ExecutionEnvironment;
import com.intellij.openapi.components.PathMacroManager;
import com.intellij.openapi.options.SettingsEditor;
import com.intellij.openapi.project.Project;
import org.jdom.Element;
import org.jetbrains.annotations.NotNull;
import java.nio.file.*;
import java.util.*;

public final class AttachConfiguration extends RunConfigurationBase<Element> {
    String buildFolder = "$PROJECT_DIR$/artifacts/ManagedGameCube";
    String targetId = "";
    final Map<String, String> sourceFileMap = new LinkedHashMap<>();
    AttachConfiguration(Project project, ConfigurationFactory factory, String name) { super(project, factory, name); }
    @Override public @NotNull SettingsEditor<? extends RunConfiguration> getConfigurationEditor() { return new AttachEditor(getProject()); }
    Path folder() { return Path.of(PathMacroManager.getInstance(getProject()).expandPath(buildFolder)); }
    Map<String, Object> attachArguments() throws Exception {
        var target = TargetStore.getInstance().find(targetId);
        if (target == null) throw new IllegalArgumentException("Select a saved target. Use Manage targets to define one on this computer.");
        var args = new LinkedHashMap<>(target.attachArguments());
        args.putAll(BuildArtifacts.discover(folder()).attachArguments());
        var mappings = new LinkedHashMap<String, String>();
        for (var entry : sourceFileMap.entrySet()) mappings.put(entry.getKey(), PathMacroManager.getInstance(getProject()).expandPath(entry.getValue()));
        args.put("sourceFileMap", mappings); return args;
    }
    @Override public void checkConfiguration() throws RuntimeConfigurationException {
        try { attachArguments(); }
        catch (Exception e) { throw new RuntimeConfigurationError(e.getMessage()); }
    }
    @Override public RunProfileState getState(@NotNull Executor executor, @NotNull ExecutionEnvironment environment) { return new RunProfileState() {
        @Override public ExecutionResult execute(Executor e, com.intellij.execution.runners.ProgramRunner<?> runner) { return null; }
    }; }
    @Override public void readExternal(@NotNull Element element) {
        super.readExternal(element);
        buildFolder = element.getAttributeValue("buildFolder", buildFolder); targetId = element.getAttributeValue("targetId", "");
        sourceFileMap.clear();
        for (var mapping : element.getChildren("sourceMapping")) sourceFileMap.put(mapping.getAttributeValue("build"), mapping.getAttributeValue("local"));
    }
    @Override public void writeExternal(@NotNull Element element) {
        super.writeExternal(element); element.setAttribute("buildFolder", buildFolder); element.setAttribute("targetId", targetId);
        for (var mapping : sourceFileMap.entrySet()) element.addContent(new Element("sourceMapping").setAttribute("build", mapping.getKey()).setAttribute("local", mapping.getValue()));
    }
    @Override public AttachConfiguration clone() {
        // Avoid sharing mutable path mappings between duplicated run configurations.
        var result = new AttachConfiguration(getProject(), getFactory(), getName());
        var state = new Element("configuration"); writeExternal(state); result.readExternal(state); return result;
    }
}
