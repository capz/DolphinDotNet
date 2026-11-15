# Sequential updater throughput changes

Stage 1 adds opt-in `make DND_BBA_TCP_WINDOW_BYTES=32768` to GameCube network
bundles. Zero (the default) retains the installed library. The updater enables
32 KiB explicitly. The native library is rebuilt from libogc2 revision
094b250f1e978d37789f18ffc2d3c9c2325062f8; the pinned toolchain's libversion.h
identifies this same revision (r2442.094b250). No binary patching or toolchain
installation changes are used. All BBA translation units use the same window.

The unscaled window is bounded to 2920..65535; 65536 is rejected. Global pools
remain at 128 pbufs of 1600 bytes, 128 TCP segments and the existing 192 KiB lwIP
heap. The window is credit, not a new per-socket array. More simultaneous queued
traffic can exhaust those existing pools and cause TCP backoff; the updater uses
one active asset stream. No managed heap increase, forced duplex or TLS change.

Validation includes a clean pinned-source rebuild, public network-header ABI
comparison, compiler/bundle build, configuration bounds checks and inspecting
the linked PowerPC TCP allocation/window-output code. An attempted raw-TCP
Dolphin debugger fixture did not complete reliably; it is not a passing packet
or hardware-throughput test. Advertised window/packet loss and sustained transfer
still require physical BBA acceptance.
