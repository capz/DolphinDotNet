using Dolphin.Storage;
using Dolphin.Diagnostics;

internal static class Program
{
    private static void PrintFiles(string heading, string[] files)
    {
        DebugOverlay.WriteLine(heading);
        for (int i=0; i<files.Length && i<8; i++)
            DebugOverlay.WriteLine(files[i]);
    }

    private static int Main()
    {
        // Try the GC Loader's SD filesystem, if the installed firmware supports
        // the libogc2 GC Loader block-device protocol.
        if (Storage.Mount(StorageDevice.GcLoader))
        {
            DebugOverlay.WriteLine("GC Loader mounted: gcloader:/");
            if (Storage.FileExists(StorageDevice.GcLoader, "games/demo.iso"))
            {
                using (var iso=GameCubeImage.Open("gcloader:/games/demo.iso"))
                {
                    PrintFiles("ISO root:",iso.GetFiles("/"));
                    var directories=iso.GetDirectories("/");
                    if (directories.Length>0)
                    {
                        var name=directories[0];
                        PrintFiles("ISO subdirectory:",iso.GetFiles(name));
                    }
                    var root=iso.GetFiles("/");
                    if(root.Length>0)
                    {
                        using var file=iso.OpenRead(root[0]);
                        var header=new byte[16];
                        int bytes=file.Read(header,0,header.Length);
                        DebugOverlay.WriteLine("Read image file bytes:");
                        DebugOverlay.WriteLine(bytes.ToString());
                    }
                }
            }
            Storage.Unmount(StorageDevice.GcLoader);
        }
        // A physically inserted game disc, or an ISO presented by a drive
        // emulator, exposes the original GameCube filesystem at dvd:/.
        if (Storage.MountGameCubeDisc())
        {
            PrintFiles("DVD game FST:",Storage.GetFiles(StorageDevice.DvdDrive,""));
            if(Storage.FileExists(StorageDevice.DvdDrive,"opening.bnr"))
            {
                var data=Storage.ReadAllBytes(StorageDevice.DvdDrive,"opening.bnr");
                DebugOverlay.WriteLine("Banner bytes:");
                DebugOverlay.WriteLine(data.Length.ToString());
            }
            Storage.Unmount(StorageDevice.DvdDrive);
        }
        return 0;
    }
}
