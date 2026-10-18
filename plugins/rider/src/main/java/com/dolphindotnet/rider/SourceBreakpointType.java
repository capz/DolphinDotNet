package com.dolphindotnet.rider;

import com.intellij.execution.RunManager;
import com.intellij.openapi.project.Project;
import com.intellij.openapi.vfs.VirtualFile;
import com.intellij.xdebugger.breakpoints.XLineBreakpointType;
import com.intellij.xdebugger.XDebuggerManager;
import java.util.Arrays;

/** Recognize SDK projects and live attach sessions without requiring a selected attach configuration. */
public final class SourceBreakpointType extends XLineBreakpointType<SourceBreakpointProperties> {
    public SourceBreakpointType() { super("dolphindotnet-source", "DolphinDotNet C# line breakpoints"); }
    @Override public boolean canPutAt(VirtualFile file, int line, Project project) {
        if (!"cs".equalsIgnoreCase(file.getExtension())) return false;
        var selected = RunManager.getInstance(project).getSelectedConfiguration();
        return (selected != null && selected.getConfiguration() instanceof AttachConfiguration)
            || Arrays.stream(XDebuggerManager.getInstance(project).getDebugSessions())
                .anyMatch(session -> session.getDebugProcess() instanceof RiderDebugProcess)
            || DolphinProject.contains(file, project);
    }
    @Override public int getPriority() { return 1000; }
    @Override public SourceBreakpointProperties createBreakpointProperties(VirtualFile file, int line) { return new SourceBreakpointProperties(); }
}
