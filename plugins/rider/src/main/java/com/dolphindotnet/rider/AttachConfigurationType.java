package com.dolphindotnet.rider;

import com.intellij.execution.configurations.*;
import com.intellij.icons.AllIcons;
import com.intellij.openapi.project.Project;
import javax.swing.*;
import org.jetbrains.annotations.NotNull;

public final class AttachConfigurationType extends ConfigurationTypeBase {
    public AttachConfigurationType() {
        super("DolphinDotNetAttach", "DolphinDotNet Attach", "Attach to an existing Dolphin or GameCube debug target", AllIcons.RunConfigurations.Remote);
        addFactory(new ConfigurationFactory(this) {
            @Override public @NotNull String getId() { return "DolphinDotNetAttach"; }
            @Override public @NotNull RunConfiguration createTemplateConfiguration(@NotNull Project project) { return new AttachConfiguration(project, this, "DolphinDotNet Attach"); }
        });
    }
}
