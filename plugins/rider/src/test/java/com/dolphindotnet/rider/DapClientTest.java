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
}
