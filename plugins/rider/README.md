# DolphinDotNet Rider plugin

Native Rider attach configuration, saved BBA/TCP and USB Gecko targets, and an
XDebugger → stdio DAP bridge with read-only primitive Arguments/Locals. Requires Rider 2026.2 (pinned build SDK 2026.2.3.1),
Java 25, .NET 8 and Python 3 for builds/tests.

```sh
./gradlew test buildPlugin verifyPlugin
```

The ZIP in `build/distributions` includes the adapter. Requires the .NET 8 runtime
on the Rider process PATH. This is an initial integration implementation;
interactive Rider and physical hardware acceptance remain pending. Debugger targets
must be Debug builds; Release target debugging is out of scope.

See [configuration, target properties, protocol and acceptance](../../docs/rider-debugging.md).
