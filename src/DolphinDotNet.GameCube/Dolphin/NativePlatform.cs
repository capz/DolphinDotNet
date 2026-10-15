using System;
namespace Dolphin;
internal static class NativePlatform
{
    private static Exception Unsupported()=>new PlatformNotSupportedException("Requires DolphinDotNet AOT.");
    internal static bool Reboot(int mode)=>throw Unsupported();
    internal static bool ResetButton()=>throw Unsupported();
}
