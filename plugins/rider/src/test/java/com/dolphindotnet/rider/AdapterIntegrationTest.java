package com.dolphindotnet.rider;

import com.google.gson.*;
import org.junit.jupiter.api.*;
import org.junit.jupiter.api.condition.*;
import org.junit.jupiter.api.io.TempDir;
import org.eclipse.lsp4j.debug.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;
import java.util.concurrent.*;
import static org.junit.jupiter.api.Assertions.*;

/** Uses the actual client and actual published .NET adapter; only RSP is fake. */
class AdapterIntegrationTest {
    @TempDir Path root;
    @Test void realTcpAdapter() throws Exception { run("tcp"); }
    @Test @EnabledOnOs(OS.LINUX) void realSerialAdapterThroughPty() throws Exception { run("usbGecko"); }
    private void run(String transport) throws Exception {
        var repository = Path.of(Objects.requireNonNull(System.getProperty("dnd.repositoryRoot"), "Missing repositoryRoot test property"));
        var adapter = Objects.requireNonNull(System.getProperty("dnd.adapter"), "Missing published adapter test property");
        var fixture = new ProcessBuilder("python3", repository.resolve("tests/rider_dap_fixture.py").toString(),
            root.resolve("build with spaces 日本").toString(), transport).redirectError(ProcessBuilder.Redirect.INHERIT).start();
        var reader = new BufferedReader(new InputStreamReader(fixture.getInputStream(), StandardCharsets.UTF_8));
        var writer = new PrintWriter(new OutputStreamWriter(fixture.getOutputStream(), StandardCharsets.UTF_8), true);
        var process = new ProcessBuilder("dotnet", adapter).start();
        var stops = new LinkedBlockingQueue<StoppedEventArguments>();
        var continued = new LinkedBlockingQueue<Boolean>();
        var client = new DapClient(process, new DapClient.Listener() {
            @Override public void stopped(StoppedEventArguments args) { stops.add(args); }
            @Override public void continued() { continued.add(true); }
            @Override public void output(String text, boolean error) { if (error) System.err.print(text); }
            @Override public void terminated() { }
        });
        try {
            var gson = new GsonBuilder().setObjectToNumberStrategy(ToNumberPolicy.LONG_OR_DOUBLE).create();
            @SuppressWarnings("unchecked") Map<String, Object> arguments = gson.fromJson(readLine(reader), Map.class);
            client.start(arguments).get(10, TimeUnit.SECONDS);
            var path = root.resolve("build with spaces 日本/日本/Game.cs").toString();
            var request = new SetBreakpointsArguments(); var source = new Source(); source.setPath(path); request.setSource(source);
            var point = new SourceBreakpoint(); point.setLine(11); request.setBreakpoints(new SourceBreakpoint[]{point});
            assertTrue(client.server.setBreakpoints(request).get(5, TimeUnit.SECONDS).getBreakpoints()[0].isVerified());
            client.server.configurationDone(new ConfigurationDoneArguments()).get(5, TimeUnit.SECONDS);
            assertEquals("entry", stopped(stops).getReason());
            var resume = new ContinueArguments(); resume.setThreadId(1); client.server.continue_(resume).get(5, TimeUnit.SECONDS);
            assertEquals("breakpoint", stopped(stops).getReason());
            var trace = new StackTraceArguments(); trace.setThreadId(1);
            var frame = client.server.stackTrace(trace).get(5, TimeUnit.SECONDS).getStackFrames()[0];
            assertEquals("Program.Main", frame.getName()); assertEquals(11, frame.getLine());
            assertEquals(path, frame.getSource().getPath());
            var step = new StepInArguments(); step.setThreadId(1); client.server.stepIn(step).get(5, TimeUnit.SECONDS);
            assertEquals("SourceLocation", stopped(stops).getDescription());
            request.setBreakpoints(new SourceBreakpoint[0]); client.server.setBreakpoints(request).get(5, TimeUnit.SECONDS);
            continued.clear(); client.server.continue_(resume).get(5, TimeUnit.SECONDS);
            assertNotNull(continued.poll(5, TimeUnit.SECONDS));
            var pause = new PauseArguments(); pause.setThreadId(1); client.server.pause(pause).get(5, TimeUnit.SECONDS);
            assertEquals("pause", stopped(stops).getReason());
            // Disconnect while executing must interrupt and remove owned breaks.
            point.setLine(999); request.setBreakpoints(new SourceBreakpoint[]{point});
            assertFalse(client.server.setBreakpoints(request).get(5, TimeUnit.SECONDS).getBreakpoints()[0].isVerified());
            continued.clear(); client.server.continue_(resume).get(5, TimeUnit.SECONDS);
            assertNotNull(continued.poll(5, TimeUnit.SECONDS));
            client.close(); assertTrue(process.waitFor(10, TimeUnit.SECONDS)); assertEquals(0, process.exitValue());
            writer.println("status"); var status = JsonParser.parseString(readLine(reader)).getAsJsonObject();
            assertEquals(0, status.getAsJsonArray("breakpoints").size());
            assertEquals(0, status.getAsJsonArray("errors").size());
            assertFalse(java.util.stream.StreamSupport.stream(status.getAsJsonArray("commands").spliterator(), false).anyMatch(c -> c.getAsString().equals("k")));
        } finally {
            client.close(); if (process.isAlive()) { process.destroyForcibly(); process.waitFor(5, TimeUnit.SECONDS); }
            writer.println("done"); writer.close();
            if (!fixture.waitFor(5, TimeUnit.SECONDS)) fixture.destroyForcibly();
            reader.close();
        }
        assertEquals(0, fixture.exitValue());
    }
    private static String readLine(BufferedReader reader) throws Exception {
        var line = CompletableFuture.supplyAsync(() -> { try { return reader.readLine(); } catch (IOException e) { throw new UncheckedIOException(e); } }).get(10, TimeUnit.SECONDS);
        return Objects.requireNonNull(line, "Fixture closed before returning JSON");
    }
    private static StoppedEventArguments stopped(BlockingQueue<StoppedEventArguments> stops) throws Exception {
        return Objects.requireNonNull(stops.poll(10, TimeUnit.SECONDS), "Missing DAP stop");
    }
}
