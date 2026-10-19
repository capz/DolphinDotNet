using System;
namespace Dolphin.IO;
internal static class NativeDol
{
    private static Exception Unsupported() => new PlatformNotSupportedException("Requires DolphinDotNet AOT.");
    internal static int DolPrepare(byte[] image) => throw Unsupported();
    internal static int DolBegin(string path) => throw Unsupported();
    internal static int DolStep(int token) => throw Unsupported();
    internal static int DolInfo(int token, int kind) => throw Unsupported();
    internal static int DolDispose(int token) => throw Unsupported();
    internal static int DolRequest(int token) => throw Unsupported();
}
