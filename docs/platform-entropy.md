# Platform entropy for a self-contained GameCube application

`Dolphin.Security.PlatformEntropy.GetBytesAsync(48)` collects a fresh TLS seed
on the SDK background executor. Its native bridge releases the managed
execution gate, so the application may continue rendering while collection is
in progress. Initialize TLS with the returned seed, then let
`TlsConfiguration.Initialize` consume and clear it. Never save the seed or
embed one in a public ISO. Public CA certificates can be embedded safely;
secret entropy cannot.

The GameCube provider uses pinned jitterentropy 3.7.0 at commit
`e783cf1c450bce4d72f95c9f9c84546a6094976a`, with the raw 40.5 MHz PowerPC timebase.
The timebase measures execution timing variation; a clock reading by itself is
not credited as random input. No MAC address, SRAM identity, calendar time,
network response, fixed constant, or persistent seed replaces the source.

The port forces upstream startup and continuous health checks, uses an
oversampling rate of 16, and disables the alternate timer/thread backend.
Upstream library translation units compile separately at `-O0 -fno-lto
-fwrapv`; the normal application can retain optimization and LTO. One
documented conditional patch disables automatic collector startup recovery:
the provider fails on the first startup health failure, without increasing
memory, hash loops, or credited sampling rates.

Each request permits 48–64 bytes, a 128 KiB maximum allocation, and a 192 KiB
total native allocation budget, plus less than 1 KiB of fixed provider state.
The provider starts fresh each time, serializes collectors, and wipes collector
state, memory-source buffers, and failed/partially generated output. Successful
output is the caller's responsibility until TLS consumes it. No seed file is
read or written.

Collection stops accepting samples at its 30-second deadline or one million
timestamp reads. Expiry returns an unusable timestamp to the source so its
health checks stop generation; output is discarded even if a call finishes
successfully after the deadline. Cleanup and the final health-check iteration
finish before the task completes. Cancellation is observed before and after
this bounded native operation; it clears temporary output rather than exposing
a partially completed seed. Caller cancellation never weakens TLS validation.

Source, deadline, allocation, and concurrent-use failures reject the request.
There is no deterministic, fixed-seed, lower-quality, or certificate-validation
fallback. A deterministic emulator or a platform with insufficient timing
variation is allowed to fail closed.

## Validation limits

Host fixtures exercise the actual library's frozen/coarse timer rejection,
allocation failures, deadline, serialization, bounds, and wiping behavior.
Synthetic clocks used to reach error paths are control-flow tests and do not
validate entropy. The build fixture checks optimization isolation and archive
cache invalidation; the native sources also compile and link against the
pinned GameCube toolchain.

**Physical GameCube entropy-source qualification remains outstanding.** Startup
and continuous health tests detect selected source failures; passing them does
not establish a minimum entropy rate. Before describing this provider as a
verified source, collect raw timing samples and restart datasets on real
GameCube hardware, apply the upstream/SP 800-90B assessment procedure, select a
conservative sampling rate from that evidence, and repeat across intended
hardware and runtime conditions. Dolphin timing tests and apparently unique
output do not substitute for this assessment.
