using System;
using System.IO;
using Dolphin.Storage;
using StorageApi=Dolphin.Storage.Storage;
internal static class Program
{
    private static int changes,lost;
    private static void Changed(MountChange change)
    {
        if(change.Reason==MountChangeReason.HistoryLost)lost++;
        else {if(change.Info==null)throw new Exception("Missing mount snapshot");changes++;}
        StorageApi.Poll(); // Reentrant dispatch is bounded and does not recurse.
    }
    public static int Main()
    {
        if(StorageApi.GetLastMount()!=null||StorageApi.GetMounts().Length!=5)return 1;
        if(StorageApi.Remount(StorageDevice.SdSerialPort2).Failure!=MountFailure.NoPreviousConfiguration)return 2;
        StorageApi.MountChanged+=Changed;
        if(!StorageApi.Mount(StorageDevice.SdSerialPort2))return 3;
        var first=StorageApi.GetMountInfo(StorageDevice.SdSerialPort2);
        if(first.State!=MountState.Mounted||first.LastSuccessfulConfiguration==null||first.RootPath!="sd:/")return 4;
        if(!StorageApi.TryGetMountedFileSystem(StorageDevice.SdSerialPort2,out var fs)||!fs!.IsValid)return 5;
        using(var stream=fs.Open("registry.bin",FileMode.Create,FileAccess.Write))stream.Write(new byte[]{7,8},0,2);
        StorageApi.Poll();if(changes!=1)return 6;
        StorageApi.Unmount(StorageDevice.SdSerialPort2);
        if(fs.IsValid||StorageApi.GetLastMount()!.State!=MountState.Unmounted)return 7;
        var remount=StorageApi.Remount(StorageDevice.SdSerialPort2);
        if(!remount.Succeeded||remount.FileSystem!.MountId==fs.MountId||fs.IsValid)return 8;
        try {using(var stale=fs.Open("registry.bin",FileMode.Open,FileAccess.Read)){}return 9;}catch(DriveNotFoundException){}
        var config=remount.FileSystem.Info.LastSuccessfulConfiguration!;
        StorageApi.Unmount(StorageDevice.SdSerialPort2);
        if(!StorageApi.Mount(config).Succeeded)return 10;
        if(StorageApi.Mount(MountConfiguration.BlockVolume(StorageDevice.SdSerialPort2,cachePages:8)).Failure!=MountFailure.ConfigurationConflict)return 11;
        if(StorageApi.GetLastMount()!.LastSuccessfulConfiguration!.CachePages!=4)return 12;
        Console.WriteLine("REMOVE_SD");StorageApi.Poll();
        if(StorageApi.GetMountInfo(StorageDevice.SdSerialPort2).State!=MountState.Unavailable)return 13;
        Console.WriteLine("RESTORE_SD");if(!StorageApi.Remount(StorageDevice.SdSerialPort2).Succeeded)return 14;
        StorageApi.Poll();
        for(int i=0;i<20;i++){StorageApi.Unmount(StorageDevice.SdSerialPort2);if(!StorageApi.Remount(StorageDevice.SdSerialPort2).Succeeded)return 15;}
        StorageApi.Poll();if(lost!=1)return 16;
        StorageApi.DeleteFile(StorageDevice.SdSerialPort2,"registry.bin");StorageApi.Unmount(StorageDevice.SdSerialPort2);
        StorageApi.Poll();StorageApi.MountChanged-=Changed;
        int delivered=changes;
        if(!StorageApi.Remount(StorageDevice.SdSerialPort2).Succeeded)return 18;
        StorageApi.Poll();if(changes!=delivered)return 19;
        StorageApi.Unmount(StorageDevice.SdSerialPort2);StorageApi.Poll();
        try {StorageApi.GetMountInfo(StorageDevice.MemoryCardSlotA);return 17;}catch(ArgumentException){}
        return 0;
    }
}
