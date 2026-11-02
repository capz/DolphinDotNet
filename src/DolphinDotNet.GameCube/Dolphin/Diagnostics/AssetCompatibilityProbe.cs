namespace Dolphin.Diagnostics;

/// <summary>GameCube debug-only result record for hardware asset regression runs.
/// The host reads the exported dnd_asset_compat_record over ordinary GDB RSP.
/// This method is a no-op in builds without DND_DEBUG_AUTOMATION.</summary>
public static class AssetCompatibilityProbe
{
    public const int Scanning=1, Ready=2, Pass=3, Unsupported=4,
        Fail=5, Empty=6, Finished=7;
    public const int Texture=1, Particle=2, Model=3;

    public static void Report(int status,int index,int total,int kind,int scanned,string name)
        => NativeAssetCompatibility.AssetCompatReport(status,index,total,kind,scanned,name);
}

internal static class NativeAssetCompatibility
{
    internal static void AssetCompatReport(int status,int index,int total,int kind,int scanned,string name)
        => throw new System.PlatformNotSupportedException("The asset compatibility probe requires GameCube AOT.");
}
