# DolphinDotNet Rider plugin

**Run → Attach to DolphinDotNet…** opens the attach dialog directly. Choose the
Debug build folder and a saved BBA/TCP or USB Gecko target, then click **Attach**.
The dialog remembers its settings per project on this machine and reuses existing
attach configuration defaults on first use when selected or unambiguous. Launch Dolphin/the device first.

C# gutter breakpoints recognize DolphinDotNet SDK projects without selecting an
attach configuration. Existing attach configurations remain available for
multilaunch workflows.

The plugin provides saved connection targets and an XDebugger → stdio DAP bridge
with read-only primitive Arguments/Locals. Requires Rider 2026.2 (pinned build SDK 2026.2.3.1),
Java 25, .NET 8 and Python 3 for builds/tests.

```sh
./gradlew test buildPlugin verifyPlugin
```

The ZIP in `build/distributions` includes the adapter. Requires the .NET 8 runtime
on the Rider process PATH. This is an initial integration implementation;
interactive Rider and physical hardware acceptance remain pending. Debugger targets
must be Debug builds; Release target debugging is out of scope.

See [configuration, target properties, protocol and acceptance](../../docs/rider-debugging.md).
