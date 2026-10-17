using System;
using System.Collections.Generic;
using Dolphin.Storage;
using Dolphin.Diagnostics;

internal static class Program
{
    private static void PrintFiles(string heading,IEnumerable<string> files)
    {
        DebugOverlay.WriteLine(heading);
        int count=0;
        foreach(var file in files)
        {
            DebugOverlay.WriteLine(file);
            if(++count==8)break;
        }
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
        PrintFiles("dvd:/ FST",Storage.EnumerateFiles(StorageDevice.DvdDrive,""));
    }
    private static int Main()
    {
        try { Browse();return 0; }
        catch(Exception error)
        {
            DebugOverlay.WriteLine("DISC BROWSE FAILED");
            DebugOverlay.WriteLine(error.Message);
            return 1;
        }
    }
    private static void Browse()
    {
        bool found=false;
        // Prefer the current game. Probing GC Loader sectors resets the drive,
        // so it must not run before browsing an already inserted disc.
        // A DOL-only Dolphin launch has no disc; mounting must fail without waiting.
        if(Storage.MountDisc())
        {
            try { ShowMountedGame(); }
            finally { Storage.Unmount(StorageDevice.DvdDrive); }
            return;
        }

        if(Storage.Mount(StorageDevice.GcLoader))
        {
            try
            {
                DebugOverlay.WriteLine("GC Loader volume: gcloader:/");
                const string image="gcloader:/games/demo.iso";
                if(Storage.FileExists(StorageDevice.GcLoader,"games/demo.iso") &&
                   Storage.MountDisc(image))
                {
                    try { ShowMountedGame();found=true; }
                    finally { Storage.Unmount(StorageDevice.DvdDrive); }
                }
            }
            finally { Storage.Unmount(StorageDevice.GcLoader); }
        }
        if(!found)
        {
            DebugOverlay.WriteLine("NO GAMECUBE DISC OR DEMO ISO AVAILABLE");
            DebugOverlay.WriteLine("INSERT A DISC OR CONFIGURE A DOLPHIN DEFAULT ISO");
        }
    }
}
