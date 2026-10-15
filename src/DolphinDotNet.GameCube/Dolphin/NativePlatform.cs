using System;
namespace Dolphin;
internal static class NativePlatform
{
    private static Exception Unsupported()=>new PlatformNotSupportedException("Requires DolphinDotNet AOT.");
    internal static bool PlatformReset(int mode)=>throw Unsupported();
    internal static bool PlatformResetButton()=>throw Unsupported();
}
