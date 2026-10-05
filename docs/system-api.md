# System API compatibility slice

This slice adds native/runtime primitives required to unblock common framework-facing builds.

## Exceptions
A runtime exception kind/message channel now covers base Exception, argument errors, invalid operation, unsupported/platform unsupported, IO/file/directory/authorization and format errors. Managed try/catch/finally lowering is still compiler work; these primitives let BCL/intrinsics report failures now.

## Guid
Provides parse, canonical D-format formatting, and NewGuid-style generation. The current GameCube entropy source is intentionally weak and is suitable for uniqueness/game data, not cryptography.

## Console
Write, WriteLine and Int32 output route through the existing overlay/platform console.

## File
Exists, ReadAllBytes and WriteAllBytes use the C/libogc stdio layer. Availability therefore depends on mounted libogc devices/filesystems.

## Path
Separator handling, GetFileName and Combine are platform-neutral and accept both slash forms.

## AppContext
BaseDirectory is currently "/" and TryGetSwitch returns false. This is enough for libraries that probe AppContext, while avoiding claims of desktop/runtime configuration support.

These are runtime primitives. Mapping normal System.* calls to them belongs in the compiler intrinsic/CoreLib layer.
