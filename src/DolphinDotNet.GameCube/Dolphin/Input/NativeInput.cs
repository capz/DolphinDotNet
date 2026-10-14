using System;

namespace Dolphin.Input;
internal static class NativeInput
{
    private static Exception Unsupported() => new PlatformNotSupportedException("Requires DolphinDotNet AOT.");
    internal static void InputUpdate() => throw Unsupported();
    internal static int InputFlags(int port) => throw Unsupported();
    internal static int InputButtons(int port, int kind) => throw Unsupported();
    internal static float InputAxis(int port, int axis) => throw Unsupported();
    internal static void InputConfigure(int port, float sd, float cd, float sm, float cm, float tm, float tx) => throw Unsupported();
    internal static bool InputRumble(int port, float seconds) => throw Unsupported();
    internal static void InputStopRumble(int port) => throw Unsupported();
}
