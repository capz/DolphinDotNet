package com.dolphindotnet.rider;

import java.util.*;

/** Machine-local connection settings. EXI slot is informational build metadata. */
public final class TargetProfile {
    public String id = UUID.randomUUID().toString();
    public String name = "New target";
    public String transport = "tcp";
    public String host = "127.0.0.1";
    public int port = 2166;
    public String serialPort = "";
    public String expectedSlot = "B";
    public int baudRate = 115200; // VCP compatibility value, not a physical USB transfer speed.
    public int timeoutSeconds = 10;
    public String heartbeatHost = "";
    public int heartbeatPort = 2167;

    public TargetProfile copy() {
        var t = new TargetProfile();
        t.id = id; t.name = name; t.transport = transport; t.host = host; t.port = port;
        t.serialPort = serialPort; t.expectedSlot = expectedSlot; t.baudRate = baudRate; t.timeoutSeconds = timeoutSeconds;
        t.heartbeatHost = heartbeatHost; t.heartbeatPort = heartbeatPort;
        return t;
    }
    public void validate() {
        if (id == null || id.isBlank() || name == null || name.isBlank()) throw new IllegalArgumentException("Target name is required.");
        if (timeoutSeconds < 1 || timeoutSeconds > 120) throw new IllegalArgumentException("Response timeout must be between 1 and 120 seconds.");
        if (heartbeatHost != null && !heartbeatHost.isBlank()) {
            var parts = heartbeatHost.trim().split("\\.", -1);
            if (parts.length != 4) throw new IllegalArgumentException("Live status needs this computer's IPv4 address reachable from the game.");
            for (var part : parts) {
                if (!part.matches("[0-9]{1,3}") || Integer.parseInt(part) > 255)
                    throw new IllegalArgumentException("Live status needs a valid IPv4 address.");
            }
            int first = Integer.parseInt(parts[0]);
            if (first == 0 || first >= 224) throw new IllegalArgumentException("Live status needs a unicast IPv4 address.");
            if (heartbeatPort < 0 || heartbeatPort > 65535) throw new IllegalArgumentException("Live status UDP port must be 0 (automatic) or between 1 and 65535.");
        }
        switch (transport) {
            case "tcp" -> {
                if (host == null || host.isBlank()) throw new IllegalArgumentException("Host is required.");
                if (port < 1 || port > 65535) throw new IllegalArgumentException("TCP port must be between 1 and 65535.");
            }
            case "usbGecko" -> {
                if (serialPort == null || serialPort.isBlank()) throw new IllegalArgumentException("Serial port is required.");
                if (baudRate <= 0) throw new IllegalArgumentException("Driver baud compatibility value must be positive.");
                if (!Set.of("A", "B").contains(expectedSlot)) throw new IllegalArgumentException("Expected console slot must be A or B.");
            }
            default -> throw new IllegalArgumentException("Choose BBA / TCP or USB Gecko.");
        }
    }
    public Map<String, Object> attachArguments() {
        validate();
        var args = new LinkedHashMap<String, Object>();
        args.put("transport", transport); args.put("timeoutSeconds", timeoutSeconds);
        if (transport.equals("tcp")) { args.put("host", host.trim()); args.put("port", port); }
        else { args.put("serialPort", serialPort.trim()); args.put("baudRate", baudRate); }
        if (heartbeatHost != null && !heartbeatHost.isBlank()) {
            args.put("heartbeatHost", heartbeatHost.trim()); args.put("heartbeatPort", heartbeatPort);
        }
        // No slot command: DEBUG_Init/EXI channel was selected when building the DOL.
        return args;
    }
    public String summary() {
        return transport.equals("tcp") ? "BBA / TCP · " + host + ":" + port
            : "USB Gecko · " + serialPort + " · Expected slot " + expectedSlot;
    }
    @Override public String toString() { return name; }
}
