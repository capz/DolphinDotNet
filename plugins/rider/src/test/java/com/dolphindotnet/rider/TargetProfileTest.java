package com.dolphindotnet.rider;

import org.junit.jupiter.api.Test;
import java.util.*;
import static org.junit.jupiter.api.Assertions.*;

class TargetProfileTest {
    @Test void liveStatusIsOptionalAndCopiesItsSeparateEndpoint() {
        var t = new TargetProfile(); t.heartbeatHost = " 192.168.1.2 "; t.heartbeatPort = 0;
        assertEquals("192.168.1.2", t.attachArguments().get("heartbeatHost"));
        assertEquals(0, t.attachArguments().get("heartbeatPort"));
        assertEquals(t.heartbeatHost, t.copy().heartbeatHost);
        assertEquals(0, t.copy().heartbeatPort);
        for (var invalid : List.of("localhost", "192.168.1.256", "192.168.1.2.", "224.0.0.1", "0.0.0.0")) {
            t.heartbeatHost = invalid; assertThrows(IllegalArgumentException.class, t::validate);
        }
        t.heartbeatHost = "192.168.1.2"; t.heartbeatPort = 65536;
        assertThrows(IllegalArgumentException.class, t::validate);
    }
    @Test void tcpArgumentsContainOnlyConnectionProperties() {
        var t = new TargetProfile(); t.name = "GameCube — BBA"; t.host = "192.168.1.80"; t.port = 2828;
        assertEquals(Map.of("transport", "tcp", "host", "192.168.1.80", "port", 2828, "timeoutSeconds", 10), t.attachArguments());
    }
    @Test void usbDoesNotSendHostPortOrBuildSlot() {
        var t = new TargetProfile(); t.transport = "usbGecko"; t.serialPort = "/dev/cu.usbserial-gecko"; t.expectedSlot = "A";
        assertEquals(Map.of("transport", "usbGecko", "serialPort", t.serialPort, "baudRate", 115200, "timeoutSeconds", 10), t.attachArguments());
    }
    @Test void rejectInvalidConnections() {
        var t = new TargetProfile(); t.host = " "; assertThrows(IllegalArgumentException.class, t::validate);
        t.host = "localhost"; t.port = 65536; assertThrows(IllegalArgumentException.class, t::validate);
        t.port = 2166; t.transport = "usbGecko"; assertThrows(IllegalArgumentException.class, t::validate);
        t.serialPort = "COM5"; t.baudRate = 0; assertThrows(IllegalArgumentException.class, t::validate);
        t.baudRate = 115200; t.expectedSlot = "C"; assertThrows(IllegalArgumentException.class, t::validate);
        t.expectedSlot = "B"; t.timeoutSeconds = 121; assertThrows(IllegalArgumentException.class, t::validate);
    }
    @Test void stableIdentitySurvivesRenameAndCopiesAreIndependent() {
        var store = new TargetStore(); var t = new TargetProfile(); store.replace(List.of(t));
        var editing = store.targets(); editing.getFirst().name = "Renamed";
        assertEquals("New target", store.find(t.id).name); // Cancel leaves the saved profile intact.
        store.replace(editing); assertEquals("Renamed", store.find(t.id).name);
        var state = store.getState(); var restored = new TargetStore(); restored.loadState(state);
        state.targets.getFirst().serialPort = "COM9";
        assertEquals("", restored.find(t.id).serialPort);
        assertThrows(IllegalArgumentException.class, () -> store.replace(List.of(t, t.copy())));
        store.replace(List.of()); assertNull(store.find(t.id)); // Deleted profile never falls back to another target.
    }
}
