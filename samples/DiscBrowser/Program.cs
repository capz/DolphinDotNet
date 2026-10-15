using Dolphin.Storage;
using Dolphin.Diagnostics;

internal static class Program
{
    private static void PrintFiles(string heading,string[] files)
    {
        DebugOverlay.WriteLine(heading);
        for(int i=0;i<files.Length && i<8;i++)DebugOverlay.WriteLine(files[i]);
    }
    private static void ShowMountedGame()
    {
        var identity=Storage.GetMountedDiscIdentity(DiscBannerLanguage.English);
        DebugOverlay.WriteLine("Game ID: "+identity.GameId);
        DebugOverlay.WriteLine("Title: "+identity.Title);
        DebugOverlay.WriteLine("Description: "+identity.ShortDescription);
        if(identity.Banner!=null)
        {
            // 96x32 RGBA pixels can also be supplied to Texture2D.Create.
            DebugOverlay.WriteLine("Banner RGBA size: "+identity.Banner.Rgba32.Length.ToString());
        }
        PrintFiles("dvd:/ FST",Storage.GetFiles(StorageDevice.DvdDrive,""));
    }
    private static int Main()
    {
        if(Storage.Mount(StorageDevice.GcLoader))
        {
            DebugOverlay.WriteLine("GC Loader volume: gcloader:/");
            const string image="gcloader:/games/demo.iso";
            if(Storage.FileExists(StorageDevice.GcLoader,"games/demo.iso") &&
               Storage.MountDisc(image))
            {
                ShowMountedGame(); // Same dvd:/ API used for a real disc.
                Storage.Unmount(StorageDevice.DvdDrive);
            }
            Storage.Unmount(StorageDevice.GcLoader);
        }
        // Real DVD and loaded ODE game both use the same dvd:/ view.
        if(Storage.MountDisc())
        {
            ShowMountedGame();
            Storage.Unmount(StorageDevice.DvdDrive);
        }
        return 0;
    }
}
