package com.dolphindotnet.rider;

import com.intellij.execution.RunManager;
import com.intellij.openapi.project.Project;
import com.intellij.openapi.vfs.VirtualFile;
import com.intellij.xdebugger.breakpoints.XLineBreakpointType;

/** Select the DolphinDotNet run configuration before adding C# gutter breakpoints. */
public final class SourceBreakpointType extends XLineBreakpointType<SourceBreakpointProperties> {
    public SourceBreakpointType() { super("dolphindotnet-source", "DolphinDotNet C# line breakpoints"); }
    @Override public boolean canPutAt(VirtualFile file, int line, Project project) {
        var selected = RunManager.getInstance(project).getSelectedConfiguration();
        return "cs".equalsIgnoreCase(file.getExtension()) && selected != null && selected.getConfiguration() instanceof AttachConfiguration;
    }
    @Override public int getPriority() { return 1000; }
    @Override public SourceBreakpointProperties createBreakpointProperties(VirtualFile file, int line) { return new SourceBreakpointProperties(); }
}
