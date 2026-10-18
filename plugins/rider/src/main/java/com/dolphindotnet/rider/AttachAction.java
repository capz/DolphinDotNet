package com.dolphindotnet.rider;

import com.intellij.execution.RunManager;
import com.intellij.execution.configurations.ConfigurationTypeUtil;
import com.intellij.openapi.actionSystem.*;
import com.intellij.openapi.project.DumbAwareAction;
import com.intellij.openapi.ui.Messages;
import org.jetbrains.annotations.NotNull;

/** Direct attach entry point; does not change the selected launch configuration. */
public final class AttachAction extends DumbAwareAction {
    @Override public @NotNull ActionUpdateThread getActionUpdateThread() { return ActionUpdateThread.BGT; }
    @Override public void update(@NotNull AnActionEvent event) {
        var project = event.getProject();
        event.getPresentation().setEnabledAndVisible(project != null && !project.isDisposed());
    }
    @Override public void actionPerformed(@NotNull AnActionEvent event) {
        var project = event.getProject();
        if (project == null || project.isDisposed()) return;
        var manager = RunManager.getInstance(project);
        var factory = ConfigurationTypeUtil.findConfigurationType(AttachConfigurationType.class).getConfigurationFactories()[0];
        var config = new AttachConfiguration(project, factory, "DolphinDotNet Attach");
        var saved = AttachSettings.getInstance(project);
        if (saved.getState().configured) saved.restore(config);
        else {
            config.buildFolder = "$PROJECT_DIR$/bin/Debug/net8.0";
            var selected = manager.getSelectedConfiguration();
            if (selected != null && selected.getConfiguration() instanceof AttachConfiguration attach) copy(attach, config);
            else {
                var existing = manager.getAllSettings().stream().map(settings -> settings.getConfiguration())
                    .filter(AttachConfiguration.class::isInstance).map(AttachConfiguration.class::cast).toList();
                if (existing.size() == 1) copy(existing.getFirst(), config);
            }
        }
        if (!new AttachDialog(project, config).showAndGet()) return;
        try {
            AttachRunner.start(project, config, null);
            saved.remember(config);
        } catch (Exception error) {
            Messages.showErrorDialog(project, error.getMessage() == null ? error.toString() : error.getMessage(), "DolphinDotNet Attach");
        }
    }
    private static void copy(AttachConfiguration source, AttachConfiguration target) {
        target.buildFolder = source.buildFolder; target.targetId = source.targetId;
        target.sourceFileMap.clear(); target.sourceFileMap.putAll(source.sourceFileMap);
    }
}
