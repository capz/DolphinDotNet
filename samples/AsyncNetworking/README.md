# Async networking and diagnostics

The example uses the deterministic local DNS/HTTP fixture described in its source:
DNS on loopback port 15353, then `http://config.test:18081`. It resolves/downloads
with cancellation, reports HTTP status, parses on `Task.Run`, and prints the value
with elapsed `Stopwatch` seconds. A scheduled 15-second watchdog cancels the
operation; failure output identifies the active stage. Networking stops in cleanup.

This is a fixture/demo endpoint, not an Internet service: change the addresses for
a real console deployment. CI executes the actual AOT sample against
`tests/network_fixtures.py`, including forced GC. See
[networking](../../docs/network-runtime.md).

