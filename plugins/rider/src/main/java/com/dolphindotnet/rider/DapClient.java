package com.dolphindotnet.rider;

import org.eclipse.lsp4j.debug.*;
import org.eclipse.lsp4j.debug.launch.DSPLauncher;
import org.eclipse.lsp4j.debug.services.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
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
    private final Listener listener;
    private final ExecutorService io = Executors.newCachedThreadPool(r -> { var t = new java.lang.Thread(r, "DolphinDotNet DAP"); t.setDaemon(true); return t; });
    private final CompletableFuture<Void> initialized = new CompletableFuture<>();
    private final AtomicBoolean started = new AtomicBoolean(), closing = new AtomicBoolean(), disposed = new AtomicBoolean(), notified = new AtomicBoolean();
    private final org.eclipse.lsp4j.jsonrpc.Launcher<IDebugProtocolServer> launcher;
    private volatile Future<?> listening;
    private volatile CompletableFuture<Void> startup;
    private final long requestMillis, disconnectMillis;
    DapClient(Process process, Listener listener) { this(process, listener, Duration.ofSeconds(130), Duration.ofSeconds(5)); }
    DapClient(Process process, Listener listener, Duration requestTimeout, Duration disconnectTimeout) {
        this.process = process; this.listener = listener;
        requestMillis = requestTimeout.toMillis(); disconnectMillis = disconnectTimeout.toMillis();
        launcher = DSPLauncher.createClientLauncher(new IDebugProtocolClient() {
            @Override public void initialized() { initialized.complete(null); }
            @Override public void stopped(StoppedEventArguments args) { if (!closing.get()) listener.stopped(args); }
            @Override public void continued(ContinuedEventArguments args) { if (!closing.get()) listener.continued(); }
            @Override public void output(OutputEventArguments args) { if (!closing.get()) listener.output(args.getOutput(), "stderr".equals(args.getCategory())); }
            @Override public void terminated(TerminatedEventArguments args) { notifyTerminated(); close(); }
        }, process.getInputStream(), process.getOutputStream(), io, consumer -> consumer);
        server = launcher.getRemoteProxy();
        // Start listeners in start(), after the owning XDebugProcess has assigned
        // this client. An immediately exiting adapter cannot call a half-built owner.
    }
    CompletableFuture<Void> start(Map<String, Object> arguments) {
        if (!started.compareAndSet(false, true) || closing.get()) return CompletableFuture.failedFuture(new IllegalStateException("Adapter session has already started or closed."));
        listening = launcher.startListening();
        io.execute(() -> {
            try (var stderr = new BufferedReader(new InputStreamReader(process.getErrorStream(), StandardCharsets.UTF_8))) {
                String line; while ((line = stderr.readLine()) != null) listener.output(line + "\n", true);
            } catch (IOException ignored) { }
        });
        io.execute(() -> {
            try { listening.get(); }
            catch (Exception error) { if (!closing.get()) listener.output("DAP transport closed: " + error.getMessage() + "\n", true); }
            finally { if (!closing.get() && !disposed.get()) { notifyTerminated(); close(); } }
        });
        process.onExit().thenRun(() -> {
            initialized.completeExceptionally(new IOException("Debug adapter exited (" + process.exitValue() + ")."));
            if (!closing.get()) notifyTerminated();
            cleanup();
        });
        var init = new InitializeRequestArguments(); init.setAdapterID("dolphindotnet"); init.setClientID("dolphindotnet-rider");
        init.setLinesStartAt1(true); init.setColumnsStartAt1(true); init.setPathFormat("path");
        startup = server.initialize(init).orTimeout(Math.min(15000, requestMillis), TimeUnit.MILLISECONDS)
            .thenCompose(capabilities -> server.attach(arguments).orTimeout(requestMillis, TimeUnit.MILLISECONDS))
            .thenCompose(ignored -> initialized.orTimeout(requestMillis, TimeUnit.MILLISECONDS));
        startup.whenComplete((ignored, error) -> { if (error != null) close(); });
        return startup;
    }
    private void notifyTerminated() { if (!closing.get() && notified.compareAndSet(false, true)) listener.terminated(); }
    @Override public void close() {
        if (!closing.compareAndSet(false, true)) return;
        initialized.completeExceptionally(new CancellationException("Debug session closed."));
        var pending = startup; if (pending != null && !pending.isDone()) pending.cancel(false);
        if (!process.isAlive() || !started.get()) { if (process.isAlive()) process.destroyForcibly(); cleanup(); return; }
        var args = new DisconnectArguments(); args.setTerminateDebuggee(false);
        // Never block the EDT. Cleanup affects the adapter only, never its target.
        server.disconnect(args).orTimeout(disconnectMillis, TimeUnit.MILLISECONDS).whenComplete((ignored, error) -> {
            try { process.getOutputStream().close(); } catch (IOException ignoredError) { }
            process.onExit().orTimeout(2, TimeUnit.SECONDS).whenComplete((exited, timeout) -> {
                if (process.isAlive()) process.destroyForcibly();
                cleanup();
            });
        });
    }
    private void cleanup() {
        if (!disposed.compareAndSet(false, true)) return;
        initialized.completeExceptionally(new IOException("Debug adapter is no longer available."));
        var reading = listening; if (reading != null) reading.cancel(true);
        io.shutdownNow();
    }
}
