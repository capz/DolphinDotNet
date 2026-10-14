using System;
namespace Dolphin.Threading.Tasks;
internal static class NativeAsync
{
    private static Exception Unsupported()=>new PlatformNotSupportedException("Requires DolphinDotNet AOT.");
    internal static bool AsyncRunning()=>throw Unsupported();
    internal static int AsyncContext()=>throw Unsupported();
    internal static void AsyncWake()=>throw Unsupported();
    internal static void AsyncProgress()=>throw Unsupported();
}
