package com.dolphindotnet.rider;

import com.intellij.execution.process.*;
import com.intellij.execution.ui.ConsoleViewContentType;
import com.intellij.icons.AllIcons;
import com.intellij.openapi.application.*;
import com.intellij.openapi.editor.*;
import com.intellij.openapi.fileTypes.*;
import com.intellij.openapi.project.Project;
import com.intellij.openapi.vfs.*;
import com.intellij.xdebugger.*;
import com.intellij.xdebugger.breakpoints.*;
import com.intellij.xdebugger.evaluation.*;
import com.intellij.xdebugger.frame.*;
import org.eclipse.lsp4j.debug.*;
import org.jetbrains.annotations.*;
import javax.swing.*;
import java.io.OutputStream;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicBoolean;

final class RiderDebugProcess extends XDebugProcess implements DapClient.Listener {
    private final DapClient dap;
    private final Map<String, Object> arguments;
    private final ExecutorService commands = Executors.newSingleThreadExecutor(r -> { var t = new java.lang.Thread(r, "DolphinDotNet debugger commands"); t.setDaemon(true); return t; });
    private final Set<XLineBreakpoint<SourceBreakpointProperties>> breakpoints = ConcurrentHashMap.newKeySet();
    private final Set<String> sentSources = new HashSet<>(); // Access only on commands executor.
    private final Map<Integer, XLineBreakpoint<SourceBreakpointProperties>> breakpointIds = new HashMap<>();
    private final AtomicBoolean ended = new AtomicBoolean();
    private final ProcessHandler handler;
    private volatile boolean ready, running;
    private volatile long generation;
    private final XBreakpointHandler<XLineBreakpoint<SourceBreakpointProperties>> breakpointHandler = new XBreakpointHandler<>(SourceBreakpointType.class) {
        @Override public void registerBreakpoint(@NotNull XLineBreakpoint<SourceBreakpointProperties> breakpoint) {
            breakpoints.add(breakpoint); updateBreakpoints();
        }
        @Override public void unregisterBreakpoint(@NotNull XLineBreakpoint<SourceBreakpointProperties> breakpoint, boolean temporary) {
            breakpoints.remove(breakpoint); updateBreakpoints();
        }
    };
    RiderDebugProcess(XDebugSession session, Process process, Map<String, Object> arguments) {
        super(session); this.arguments = arguments;
        handler = new ProcessHandler() {
            { process.onExit().thenRun(() -> notifyProcessTerminated(process.exitValue())); }
            @Override protected void destroyProcessImpl() { stop(); }
            @Override protected void detachProcessImpl() { stop(); }
            @Override public boolean detachIsDefault() { return false; }
            @Override public OutputStream getProcessInput() { return null; } // No console typing into DAP stdin.
        };
        dap = new DapClient(process, this);
        session.setPauseActionSupported(true);
    }
    @Override protected ProcessHandler doGetProcessHandler() { return handler; }
    @Override public @NotNull XBreakpointHandler<?>[] getBreakpointHandlers() { return new XBreakpointHandler<?>[]{breakpointHandler}; }
    @Override public @NotNull XDebuggerEditorsProvider getEditorsProvider() {
        return new XDebuggerEditorsProvider() {
            @Override public @NotNull FileType getFileType() { return PlainTextFileType.INSTANCE; }
            @Override public boolean isEvaluateExpressionFieldEnabled() { return false; }
            @Override public @NotNull Document createDocument(@NotNull Project project, @NotNull String text, XSourcePosition position, @NotNull EvaluationMode mode) {
                return EditorFactory.getInstance().createDocument(text);
            }
        };
    }
    @Override public void sessionInitialized() {
        submit(() -> {
            await(dap.start(arguments)); ready = true; syncBreakpoints();
            await(dap.server.configurationDone(new ConfigurationDoneArguments()));
        });
    }
    private void updateBreakpoints() {
        if (ended.get()) return;
        submit(() -> { if (ready && !running) syncBreakpoints(); });
    }
    private void syncBreakpoints() throws Exception {
        var desired = new LinkedHashMap<String, List<XLineBreakpoint<SourceBreakpointProperties>>>();
        for (var bp : breakpoints) {
            if (!bp.isEnabled()) continue;
            var position = bp.getSourcePosition();
            if (position != null) desired.computeIfAbsent(position.getFile().getPath(), key -> new ArrayList<>()).add(bp);
        }
        var sources = new HashSet<>(sentSources); sources.addAll(desired.keySet()); breakpointIds.clear();
        for (var path : sources) {
            var entries = desired.getOrDefault(path, List.of());
            // Conditional/log breakpoints are explicitly unsupported, never silently downgraded.
            var supported = entries.stream().filter(bp -> {
                boolean ok = bp.getConditionExpression() == null && bp.getLogExpressionObject() == null && !bp.isLogMessage();
                if (!ok) ui(() -> getSession().updateBreakpointPresentation(bp, AllIcons.Debugger.Db_invalid_breakpoint, "Conditional breakpoints and logpoints are unsupported."));
                return ok;
            }).toList();
            var request = new SetBreakpointsArguments(); var source = new Source(); source.setPath(path); request.setSource(source);
            request.setBreakpoints(supported.stream().map(bp -> { var p = new SourceBreakpoint(); p.setLine(bp.getLine() + 1); return p; }).toArray(SourceBreakpoint[]::new));
            var response = await(dap.server.setBreakpoints(request)).getBreakpoints();
            for (int i = 0; i < supported.size(); i++) {
                var bp = supported.get(i); var result = response[i];
                if (result.getId() != null) breakpointIds.put(result.getId(), bp);
                ui(() -> getSession().updateBreakpointPresentation(bp, result.isVerified() ? AllIcons.Debugger.Db_verified_breakpoint : AllIcons.Debugger.Db_invalid_breakpoint, result.getMessage()));
            }
        }
        sentSources.clear(); sentSources.addAll(desired.keySet());
    }
    @Override public void stopped(StoppedEventArguments args) {
        running = false; long stopGeneration = ++generation;
        submit(() -> {
            if (!ready) return;
            syncBreakpoints();
            var request = new StackTraceArguments(); request.setThreadId(1);
            var frames = await(dap.server.stackTrace(request)).getStackFrames();
            var context = new TargetSuspendContext(frames);
            var hit = args.getHitBreakpointIds();
            var breakpoint = hit == null ? null : Arrays.stream(hit).map(breakpointIds::get).filter(Objects::nonNull).findFirst().orElse(null);
            ui(() -> {
                if (generation != stopGeneration) return;
                if (breakpoint != null) getSession().breakpointReached(breakpoint, null, context);
                else getSession().positionReached(context);
            });
            var description = args.getDescription();
            if (description != null && !description.equals("SourceLocation") && !description.equals("UserBreakpoint"))
                output(description + "\n", false);
        });
    }
    @Override public void continued() { running = true; ++generation; }
    @Override public void output(String text, boolean error) {
        if (text != null) ui(() -> getSession().getConsoleView().print(text, error ? ConsoleViewContentType.ERROR_OUTPUT : ConsoleViewContentType.NORMAL_OUTPUT));
    }
    @Override public void terminated() { stop(); }
    @Override public void stop() {
        if (!ended.compareAndSet(false, true)) return;
        ++generation; commands.shutdownNow(); dap.close(); ui(() -> getSession().stop());
    }
    @Override public boolean checkCanPerformCommands() { return ready && !ended.get(); }
    @Override public void startPausing() {
        if (!ready || !running) return;
        submit(() -> { var args = new PauseArguments(); args.setThreadId(1); await(dap.server.pause(args)); });
    }
    @Override public void resume(@Nullable XSuspendContext context) { execute("continue"); }
    @Override public void startStepInto(@Nullable XSuspendContext context) { execute("stepIn"); }
    @Override public void startStepOver(@Nullable XSuspendContext context) { execute("next"); }
    @Override public void startStepOut(@Nullable XSuspendContext context) { execute("stepOut"); }
    private void execute(String command) {
        if (!ready || running) return;
        submit(() -> {
            if (!ready || running) return;
            syncBreakpoints(); running = true; ++generation;
            switch (command) {
                case "continue" -> { var a = new ContinueArguments(); a.setThreadId(1); await(dap.server.continue_(a)); }
                case "stepIn" -> { var a = new StepInArguments(); a.setThreadId(1); await(dap.server.stepIn(a)); }
                case "next" -> { var a = new NextArguments(); a.setThreadId(1); await(dap.server.next(a)); }
                default -> { var a = new StepOutArguments(); a.setThreadId(1); await(dap.server.stepOut(a)); }
            }
        });
    }
    private void ui(Runnable action) {
        ApplicationManager.getApplication().invokeLater(() -> { if (!getSession().getProject().isDisposed()) action.run(); });
    }
    @FunctionalInterface private interface Command { void run() throws Exception; }
    private void submit(Command action) {
        if (ended.get()) return;
        try { commands.execute(() -> {
            if (ended.get()) return;
            try { action.run(); }
            catch (Exception e) { if (!ended.get()) { output("DolphinDotNet: " + e.getMessage() + "\n", true); stop(); } }
        }); } catch (RejectedExecutionException ignored) { }
    }
    private static <T> T await(CompletableFuture<T> future) throws Exception { return future.get(135, TimeUnit.SECONDS); }

    private final class TargetSuspendContext extends XSuspendContext {
        private final XExecutionStack stack;
        TargetSuspendContext(StackFrame[] frames) {
            var converted = Arrays.stream(frames).map(TargetFrame::new).map(f -> (XStackFrame)f).toList();
            stack = new XExecutionStack("PowerPC target (all-stop)") {
                @Override public XStackFrame getTopFrame() { return converted.isEmpty() ? null : converted.getFirst(); }
                @Override public void computeStackFrames(int first, @NotNull XStackFrameContainer container) {
                    if (!container.isObsolete()) container.addStackFrames(converted.subList(Math.min(first, converted.size()), converted.size()), true);
                }
            };
        }
        @Override public XExecutionStack getActiveExecutionStack() { return stack; }
        @Override public XExecutionStack @NotNull [] getExecutionStacks() { return new XExecutionStack[]{stack}; }
    }
    private final class TargetFrame extends XStackFrame {
        private final StackFrame frame;
        private final long frameGeneration = generation;
        TargetFrame(StackFrame frame) { this.frame = frame; }
        @Override public XSourcePosition getSourcePosition() {
            if (frame.getSource() == null || frame.getSource().getPath() == null || frame.getLine() < 1) return null;
            var file = LocalFileSystem.getInstance().findFileByPath(frame.getSource().getPath());
            return file == null ? null : XDebuggerUtil.getInstance().createPosition(file, frame.getLine() - 1);
        }
        @Override public void customizePresentation(@NotNull com.intellij.ui.ColoredTextContainer component) {
            component.append(frame.getName(), com.intellij.ui.SimpleTextAttributes.REGULAR_ATTRIBUTES);
            if (frame.getLine() > 0) component.append(":" + frame.getLine(), com.intellij.ui.SimpleTextAttributes.GRAYED_ATTRIBUTES);
        }
        @Override public void computeChildren(@NotNull XCompositeNode node) {
            submit(() -> {
                if (node.isObsolete()) return;
                if (running || generation != frameGeneration) { node.setErrorMessage("Frame is no longer stopped."); return; }
                var request = new ScopesArguments(); request.setFrameId(frame.getId());
                var children = new XValueChildrenList();
                for (var scope : await(dap.server.scopes(request)).getScopes()) {
                    children.addTopGroup(new XValueGroup(scope.getName()) {
                        @Override public boolean isAutoExpand() { return true; }
                        @Override public void computeChildren(@NotNull XCompositeNode target) {
                            loadChildren(scope.getVariablesReference(), scope.getNamedVariables(), target, frameGeneration);
                        }
                    });
                }
                if (!node.isObsolete() && generation == frameGeneration) node.addChildren(children, true);
            });
        }
    }

    private void loadChildren(int reference, Integer total, XCompositeNode node, long stopGeneration) {
        if (total == null || total <= 128) { loadPage(reference, 0, node, stopGeneration); return; }
        submit(() -> {
            if (node.isObsolete()) return;
            if (running || generation != stopGeneration) { node.setErrorMessage("Frame is no longer stopped."); return; }
            var pages = new XValueChildrenList();
            for (int offset = 0; offset < Math.min(total, 4096); offset += 128) {
                final int start = offset;
                pages.addTopGroup(new XValueGroup("Variables " + (start + 1) + "–" + Math.min(start + 128, total)) {
                    @Override public void computeChildren(@NotNull XCompositeNode target) { loadPage(reference, start, target, stopGeneration); }
                });
            }
            if (!node.isObsolete() && generation == stopGeneration) node.addChildren(pages, true);
        });
    }

    private void loadPage(int reference, int start, XCompositeNode node, long stopGeneration) {
        submit(() -> {
            if (node.isObsolete()) return;
            if (running || generation != stopGeneration) { node.setErrorMessage("Frame is no longer stopped."); return; }
            var args = new VariablesArguments(); args.setVariablesReference(reference); args.setStart(start); args.setCount(128);
            var values = new XValueChildrenList();
            for (var variable : await(dap.server.variables(args)).getVariables()) {
                values.add(variable.getName(), new XValue() {
                    @Override public void computePresentation(@NotNull XValueNode value, @NotNull XValuePlace place) {
                        value.setPresentation(null, variable.getType(), variable.getValue(), variable.getVariablesReference() != 0);
                    }
                    @Override public void computeChildren(@NotNull XCompositeNode target) {
                        loadChildren(variable.getVariablesReference(), variable.getNamedVariables(), target, stopGeneration);
                    }
                });
            }
            if (!node.isObsolete() && generation == stopGeneration) node.addChildren(values, true);
        });
    }
}
