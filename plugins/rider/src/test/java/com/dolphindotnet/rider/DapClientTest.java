package com.dolphindotnet.rider;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;
import org.eclipse.lsp4j.debug.*;
import java.nio.file.*;
import java.util.*;
import java.util.concurrent.*;
import static org.junit.jupiter.api.Assertions.*;

class DapClientTest {
    @TempDir Path temp;
    @Test void initializeAttachConfigurePauseStackStepsAndDisconnect() throws Exception {
        Path peer = temp.resolve("peer.py"), log = temp.resolve("requests.jsonl");
        try (var input = getClass().getResourceAsStream("/fake_adapter.py")) { Files.copy(Objects.requireNonNull(input), peer); }
        var process = new ProcessBuilder("python3", peer.toString(), log.toString()).start();
        var stops = new LinkedBlockingQueue<StoppedEventArguments>();
        var resumed = new CountDownLatch(1);
        var client = new DapClient(process, new DapClient.Listener() {
            @Override public void stopped(StoppedEventArguments args) { stops.add(args); }
            @Override public void continued() { resumed.countDown(); }
            @Override public void output(String text, boolean error) { }
            @Override public void terminated() { }
        });
        try {
            var target = new TargetProfile(); target.transport = "usbGecko"; target.serialPort = "COM5";
            client.start(target.attachArguments()).get(10, TimeUnit.SECONDS);
            var bp = new SetBreakpointsArguments(); var source = new Source(); source.setPath("/source/Game.cs"); bp.setSource(source);
            var point = new SourceBreakpoint(); point.setLine(12); bp.setBreakpoints(new SourceBreakpoint[]{point});
            assertTrue(client.server.setBreakpoints(bp).get(5, TimeUnit.SECONDS).getBreakpoints()[0].isVerified());
            client.server.configurationDone(new ConfigurationDoneArguments()).get(5, TimeUnit.SECONDS);
            assertEquals("entry", Objects.requireNonNull(stops.poll(5, TimeUnit.SECONDS)).getReason());
            var resume = new ContinueArguments(); resume.setThreadId(1); client.server.continue_(resume).get(5, TimeUnit.SECONDS);
            assertTrue(resumed.await(5, TimeUnit.SECONDS));
            var pause = new PauseArguments(); pause.setThreadId(1); client.server.pause(pause).get(5, TimeUnit.SECONDS);
            assertEquals("pause", Objects.requireNonNull(stops.poll(5, TimeUnit.SECONDS)).getReason());
            var stack = new StackTraceArguments(); stack.setThreadId(1);
            assertEquals(12, client.server.stackTrace(stack).get(5, TimeUnit.SECONDS).getStackFrames()[0].getLine());
            var into = new StepInArguments(); into.setThreadId(1); client.server.stepIn(into).get(5, TimeUnit.SECONDS);
            assertNotNull(stops.poll(5, TimeUnit.SECONDS));
            var over = new NextArguments(); over.setThreadId(1); client.server.next(over).get(5, TimeUnit.SECONDS);
            assertNotNull(stops.poll(5, TimeUnit.SECONDS));
            var out = new StepOutArguments(); out.setThreadId(1); client.server.stepOut(out).get(5, TimeUnit.SECONDS);
            assertNotNull(stops.poll(5, TimeUnit.SECONDS));
        } finally { client.close(); }
        assertTrue(process.waitFor(10, TimeUnit.SECONDS)); assertEquals(0, process.exitValue());
        var transcript = Files.readString(log);
        assertTrue(transcript.indexOf("initialize") < transcript.indexOf("attach"));
        assertTrue(transcript.indexOf("setBreakpoints") < transcript.indexOf("configurationDone"));
        assertTrue(transcript.contains("\"serialPort\": \"COM5\""));
        assertFalse(transcript.contains("expectedSlot"));
        assertTrue(transcript.contains("\"terminateDebuggee\": false"));
    }
    @Test void startupFailureAndCancellationReapOnlyTheAdapter() throws Exception {
        for (var mode : List.of("earlyExit", "closeStdout", "unresponsive", "rejectAttach", "missingInitialized", "cancel")) {
            Path peer = temp.resolve(mode + ".py");
            try (var input = getClass().getResourceAsStream("/fake_adapter.py")) { Files.copy(Objects.requireNonNull(input), peer); }
            var process = new ProcessBuilder("python3", peer.toString(), temp.resolve(mode + ".jsonl").toString(),
                mode.equals("cancel") ? "unresponsive" : mode).start();
            var terminated = new java.util.concurrent.atomic.AtomicInteger();
            var client = new DapClient(process, new DapClient.Listener() {
                @Override public void stopped(StoppedEventArguments args) { fail("Startup failure must not publish a usable stop."); }
                @Override public void continued() { fail("Startup failure must not resume anything."); }
                @Override public void output(String text, boolean error) { }
                @Override public void terminated() { terminated.incrementAndGet(); }
            }, java.time.Duration.ofMillis(250), java.time.Duration.ofMillis(250));
            try {
                var pending = client.start(new TargetProfile().attachArguments());
                if (mode.equals("cancel")) pending.cancel(false);
                assertThrows(Exception.class, () -> pending.get(5, TimeUnit.SECONDS), mode);
                client.close(); client.close();
                assertTrue(process.waitFor(8, TimeUnit.SECONDS), "Orphaned adapter: " + mode);
                assertTrue(terminated.get() <= 1, "Duplicate termination: " + mode);
            } finally { client.close(); if (process.isAlive()) process.destroyForcibly(); }
        }
    }

    @Test void crashAfterAttachTerminatesOnceAndReapsProcess() throws Exception {
        Path peer = temp.resolve("crash.py");
        try (var input = getClass().getResourceAsStream("/fake_adapter.py")) { Files.copy(Objects.requireNonNull(input), peer); }
        var process = new ProcessBuilder("python3", peer.toString(), temp.resolve("crash.jsonl").toString(), "crashAfterConfigure").start();
        var count = new java.util.concurrent.atomic.AtomicInteger();
        var ended = new CountDownLatch(1);
        var client = new DapClient(process, new DapClient.Listener() {
            @Override public void stopped(StoppedEventArguments args) { }
            @Override public void continued() { }
            @Override public void output(String text, boolean error) { }
            @Override public void terminated() { count.incrementAndGet(); ended.countDown(); }
        });
        try {
            client.start(new TargetProfile().attachArguments()).get(5, TimeUnit.SECONDS);
            client.server.configurationDone(new ConfigurationDoneArguments()).get(5, TimeUnit.SECONDS);
            assertTrue(ended.await(5, TimeUnit.SECONDS));
            assertTrue(process.waitFor(5, TimeUnit.SECONDS));
            assertEquals(9, process.exitValue());
            client.close(); client.close();
            assertEquals(1, count.get());
        } finally { client.close(); if (process.isAlive()) process.destroyForcibly(); }
    }

}
