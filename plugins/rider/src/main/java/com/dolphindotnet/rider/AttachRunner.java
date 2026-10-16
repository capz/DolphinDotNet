package com.dolphindotnet.rider;

import com.intellij.execution.*;
import com.intellij.execution.configurations.*;
import com.intellij.execution.executors.DefaultDebugExecutor;
import com.intellij.execution.runners.*;
import com.intellij.execution.ui.RunContentDescriptor;
import com.intellij.xdebugger.*;
import org.jetbrains.annotations.NotNull;

public final class AttachRunner extends GenericProgramRunner<RunnerSettings> {
    @Override public @NotNull String getRunnerId() { return "DolphinDotNetAttachRunner"; }
    @Override public boolean canRun(@NotNull String executorId, @NotNull RunProfile profile) {
        return DefaultDebugExecutor.EXECUTOR_ID.equals(executorId) && profile instanceof AttachConfiguration;
    }
    @Override protected RunContentDescriptor doExecute(@NotNull RunProfileState state, @NotNull ExecutionEnvironment environment) throws ExecutionException {
        var configuration = (AttachConfiguration)environment.getRunProfile();
        final java.util.Map<String, Object> args;
        try { args = configuration.attachArguments(); } catch (Exception e) { throw new ExecutionException(e.getMessage(), e); }
        return XDebuggerManager.getInstance(environment.getProject()).newSessionBuilder(new XDebugProcessStarter() {
            @Override public @NotNull XDebugProcess start(@NotNull XDebugSession session) throws ExecutionException {
                return new RiderDebugProcess(session, AdapterCommand.create().createProcess(), args);
            }
        }).environment(environment).sessionName(configuration.getName()).showTab(true).startSession().getRunContentDescriptor();
    }
}
