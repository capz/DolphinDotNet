using System;
namespace Dolphin.Threading.Tasks;
internal static class NativeAsync
{
    internal static void AsyncProductiveWaits(int enabled)=>throw Unsupported();
    internal static void AsyncPollStride(int stride)=>throw Unsupported();
    internal static long AsyncPollStat(int kind)=>throw Unsupported();
    private static Exception Unsupported()=>new PlatformNotSupportedException("Requires DolphinDotNet AOT.");
    internal static bool AsyncRunning()=>throw Unsupported();
    internal static int AsyncContext()=>throw Unsupported();
    internal static void AsyncWake()=>throw Unsupported();
    internal static void AsyncService()=>throw Unsupported();
    internal static void AsyncWait()=>throw Unsupported();
    internal static void AsyncWaitReset()=>throw Unsupported();
    internal static void AsyncWaitSkipped()=>throw Unsupported();
    internal static void AsyncDeadline(long due)=>throw Unsupported();
    internal static void AsyncDeadlineWaits(int enabled)=>throw Unsupported();
    internal static void AsyncTimerLate(long late)=>throw Unsupported();
    internal static void AsyncWaitSnapshot(long[] destination)=>throw Unsupported();
    internal static long AsyncWaitStat(int kind)=>throw Unsupported();
    internal static void AsyncProgress()=>throw Unsupported();
}
