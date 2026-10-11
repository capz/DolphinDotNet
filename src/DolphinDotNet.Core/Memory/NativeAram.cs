using System;
namespace Dolphin.Memory;
internal static class NativeAram
{
    private static Exception Unsupported()=>new PlatformNotSupportedException("Requires DolphinDotNet AOT.");
    internal static bool AramInitialize(int reserved)=>throw Unsupported();
    internal static bool AramAvailable()=>throw Unsupported();
    internal static int AramStat(int kind)=>throw Unsupported();
    internal static int AramAllocate(int length)=>throw Unsupported();
    internal static int AramRetain(int token)=>throw Unsupported();
    internal static int AramRelease(int token)=>throw Unsupported();
    internal static int AramDispose(int token)=>throw Unsupported();
    internal static int AramBegin(int token,int offset,byte[] array,int arrayOffset,int count,int write)=>throw Unsupported();
    internal static int AramPoll(byte[] array,int arrayOffset)=>throw Unsupported();
}
