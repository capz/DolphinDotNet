package com.dolphindotnet.rider;

import com.intellij.openapi.components.*;
import com.intellij.openapi.project.Project;
import org.jetbrains.annotations.NotNull;
import java.util.LinkedHashMap;
import java.util.Map;

/** Machine-local per-project dialog preferences, independent of RunManager selection. */
@Service(Service.Level.PROJECT)
@State(name = "DolphinDotNetAttach", storages = @Storage(StoragePathMacros.WORKSPACE_FILE))
public final class AttachSettings implements PersistentStateComponent<AttachSettings.Data> {
    public static final class Data {
        public boolean configured;
        public String buildFolder = "$PROJECT_DIR$/bin/Debug/net8.0", targetId = "";
        public Map<String, String> sourceFileMap = new LinkedHashMap<>();
    }
    private volatile Data data = new Data();
    static AttachSettings getInstance(Project project) { return project.getService(AttachSettings.class); }
    @Override public @NotNull Data getState() { return data; }
    @Override public void loadState(@NotNull Data state) { data = state; }
    void restore(AttachConfiguration config) {
        config.buildFolder = data.buildFolder; config.targetId = data.targetId;
        config.sourceFileMap.clear(); config.sourceFileMap.putAll(data.sourceFileMap);
    }
    void remember(AttachConfiguration config) {
        var next = new Data(); next.configured = true;
        next.buildFolder = config.buildFolder; next.targetId = config.targetId;
        next.sourceFileMap.putAll(config.sourceFileMap); data = next;
    }
}
