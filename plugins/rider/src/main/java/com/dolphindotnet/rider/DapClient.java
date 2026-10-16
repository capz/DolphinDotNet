package com.dolphindotnet.rider;

import org.eclipse.lsp4j.debug.*;
import org.eclipse.lsp4j.debug.launch.DSPLauncher;
import org.eclipse.lsp4j.debug.services.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.Map;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicBoolean;

/** Owns only the adapter process. stdout is reserved exclusively for DAP framing. */
final class DapClient implements AutoCloseable {
    interface Listener {
        void stopped(StoppedEventArguments args);
        void continued();
        void output(String text, boolean error);
        void terminated();
    }
    final IDebugProtocolServer server;
    private final Process process;
    private final ExecutorService io = Executors.newCachedThreadPool(r -> { var t = new java.lang.Thread(r, "DolphinDotNet DAP"); t.setDaemon(true); return t; });
    private final CompletableFuture<Void> initialized = new CompletableFuture<>();
    private final AtomicBoolean closed = new AtomicBoolean();
    private final Future<?> listening;
    DapClient(Process process, Listener listener) {
        this.process = process;
        var launcher = DSPLauncher.createClientLauncher(new IDebugProtocolClient() {
            @Override public void initialized() { initialized.complete(null); }
            @Override public void stopped(StoppedEventArguments args) { listener.stopped(args); }
            @Override public void continued(ContinuedEventArguments args) { listener.continued(); }
            @Override public void output(OutputEventArguments args) { listener.output(args.getOutput(), "stderr".equals(args.getCategory())); }
            @Override public void terminated(TerminatedEventArguments args) { listener.terminated(); }
        }, process.getInputStream(), process.getOutputStream(), io, consumer -> consumer);
        server = launcher.getRemoteProxy(); listening = launcher.startListening();
        io.execute(() -> {
            try (var stderr = new BufferedReader(new InputStreamReader(process.getErrorStream(), StandardCharsets.UTF_8))) {
                String line; while ((line = stderr.readLine()) != null) listener.output(line + "\n", true);
            } catch (IOException ignored) { }
        });
        process.onExit().thenRun(() -> {
            initialized.completeExceptionally(new IOException("Debug adapter exited (" + process.exitValue() + ")."));
            if (!closed.get()) listener.terminated();
        });
    }
    CompletableFuture<Void> start(Map<String, Object> arguments) {
        var init = new InitializeRequestArguments(); init.setAdapterID("dolphindotnet"); init.setClientID("dolphindotnet-rider");
        init.setLinesStartAt1(true); init.setColumnsStartAt1(true); init.setPathFormat("path");
        return server.initialize(init).orTimeout(15, TimeUnit.SECONDS)
            .thenCompose(capabilities -> server.attach(arguments).orTimeout(130, TimeUnit.SECONDS))
            .thenCompose(ignored -> initialized.orTimeout(15, TimeUnit.SECONDS));
    }
    @Override public void close() {
        if (!closed.compareAndSet(false, true)) return;
        var args = new DisconnectArguments(); args.setTerminateDebuggee(false);
        // Never block the EDT. The adapter interrupts/cleans up; only it is killed on timeout.
        server.disconnect(args).orTimeout(5, TimeUnit.SECONDS).whenComplete((ignored, error) -> {
            try { process.getOutputStream().close(); } catch (IOException ignoredError) { }
            process.onExit().orTimeout(2, TimeUnit.SECONDS).whenComplete((exited, timeout) -> {
                if (process.isAlive()) process.destroyForcibly();
                listening.cancel(true); io.shutdownNow();
            });
        });
    }
}
