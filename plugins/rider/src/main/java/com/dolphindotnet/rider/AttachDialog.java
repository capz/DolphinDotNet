package com.dolphindotnet.rider;

import com.intellij.openapi.project.Project;
import com.intellij.openapi.ui.DialogWrapper;
import com.intellij.openapi.util.Disposer;
import org.jetbrains.annotations.Nullable;
import javax.swing.JComponent;

final class AttachDialog extends DialogWrapper {
    private final AttachConfiguration configuration;
    private final AttachEditor editor;
    AttachDialog(Project project, AttachConfiguration configuration) {
        super(project);
        this.configuration = configuration;
        editor = new AttachEditor(project);
        editor.getComponent(); editor.resetFrom(configuration);
        Disposer.register(getDisposable(), editor);
        setTitle("Attach to DolphinDotNet"); setOKButtonText("Attach"); init();
    }
    @Override protected @Nullable JComponent createCenterPanel() { return editor.getComponent(); }
    @Override protected String getDimensionServiceKey() { return "DolphinDotNet.AttachDialog"; }
    @Override protected void doOKAction() {
        try {
            editor.applyTo(configuration); configuration.checkConfiguration(); super.doOKAction();
        } catch (Exception error) { setErrorText(error.getMessage()); }
    }
}
