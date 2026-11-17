# Incoming launcher context

GameCube applications can read `Dolphin.IO.LaunchContext.ExecutablePath`,
`ArgumentCount`, `GetArgument(index)` and `GetArguments()`. This is independent
of the managed Main signature. `GetArguments()` returns a new array.

The SDK snapshots libogc CRT argc/argv before platform or managed initialization.
It retains at most 64 arguments and 4096 UTF-8 bytes, validates cached MEM1 ranges
on GameCube and rejects incomplete or oversized input atomically. Missing or
invalid input produces zero arguments and an empty executable path. The source
buffer can be reused after startup. Unused applications do not enable capture
or retain its approximately 4 KiB buffer. Wii and Wii U are unsupported.

The path is supplied by the launcher. Applications must validate device aliases,
mount the corresponding device and verify their package identity and content;
the SDK does not infer a working directory, mount devices or certify a path.
Loaders with no usable arguments need an application-defined bounded fallback.

Native and real AOT host fixtures cover missing arguments, UTF-8, source-buffer
reuse, bounds, MEM1 address limits, the 64/4096 ceilings and GC stress. Actual
launcher-to-application handoff still requires hardware qualification.
