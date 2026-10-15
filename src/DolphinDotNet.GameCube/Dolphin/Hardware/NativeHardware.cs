using System;
namespace Dolphin.Hardware;
internal static class NativeHardware
{
    private static Exception Unsupported()=>new PlatformNotSupportedException("Requires DolphinDotNet AOT.");
    internal static int Probe(int kind,int port,int mode)=>throw Unsupported();
    internal static int Id(int kind,int port)=>throw Unsupported();
}
