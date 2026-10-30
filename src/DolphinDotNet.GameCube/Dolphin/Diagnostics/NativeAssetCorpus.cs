using System;

namespace Dolphin.Diagnostics;

/// <summary>Fixture-only AOT bridge for the debugger's read-only result record.
/// The host reads this through RSP at GX frame boundaries, not via SDK IO.</summary>
public static class NativeAssetCorpus
{
    public static void Publish(int kind,int result,int sourceBytes,int nativeBytes,string path)
        => NativeCorpusBridge.Publish(kind,result,sourceBytes,nativeBytes,path);
    public static void Finish(int discCount,int scanned,int candidates,int completed)
        => NativeCorpusBridge.Finish(discCount,scanned,candidates,completed);
}
internal static class NativeCorpusBridge
{
    internal static void Publish(int kind,int result,int sourceBytes,int nativeBytes,string path)
        => throw new PlatformNotSupportedException("Requires GameCube AOT.");
    internal static void Finish(int discCount,int scanned,int candidates,int completed)
        => throw new PlatformNotSupportedException("Requires GameCube AOT.");
}
