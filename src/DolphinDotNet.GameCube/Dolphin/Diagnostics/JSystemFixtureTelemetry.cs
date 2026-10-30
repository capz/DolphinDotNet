using System;
namespace Dolphin.Diagnostics;

/// <summary>Test-DOL-only debugger status bridge, not a runtime game UI or
/// proprietary asset exporter. Requires a GameCube debug-symbol AOT build.</summary>
public static class JSystemFixtureTelemetry
{
    public const int Scanning=1, Ready=2, Showing=3, Done=4, Unavailable=5;
    public static void Publish(int phase,int sourceIndex,int sourceCount,
        int assetIndex,int assetCount,int passed,int skipped,int failed,
        int kind,int detail,string path)
    {
        if(path==null)throw new ArgumentNullException(nameof(path));
        NativeJSystemFixture.FixtureReport(phase,sourceIndex,sourceCount,assetIndex,
            assetCount,passed,skipped,failed,kind,detail,path);
    }
}
internal static class NativeJSystemFixture
{
    internal static void FixtureReport(int phase,int sourceIndex,int sourceCount,
        int assetIndex,int assetCount,int passed,int skipped,int failed,
        int kind,int detail,string path)
        => throw new PlatformNotSupportedException("Requires GameCube debug-symbol AOT.");
}
