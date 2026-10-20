using Dolphin.Storage;

namespace Dolphin.IO;

/// <summary>
/// Controls automatic contiguous allocation and selective fragmentation repair
/// for managed file writes to GameCube SD volumes.
/// </summary>
public static class Defrag
{
    /// <summary>False until explicitly enabled for the current process.</summary>
    public static bool IsEnabled => NativeStorage.AutoDefragEnabled();

    /// <summary>
    /// Opt in to automatic contiguous preallocation for known-length writes
    /// and selective optimization of files after their final write handle
    /// closes. Turn off before a write to use ordinary filesystem behavior.
    /// Does not affect explicit Storage.CreateContiguousFile calls.
    /// </summary>
    public static void OptIn(bool enabled) => NativeStorage.SetAutoDefrag(enabled);
}
