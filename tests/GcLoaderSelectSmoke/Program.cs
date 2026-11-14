using System;
using StorageApi=Dolphin.Storage.Storage;

internal static class Program
{
    public static int Main()
    {
        try { StorageApi.SelectGcLoaderDiscImage(null!);return 1; }
        catch(ArgumentNullException) {}
        try { StorageApi.SelectGcLoaderDiscImage("gcloader:/new.iso");return 2; }
        catch(NotSupportedException error) {
            if(error.Message!="Physical GC Loader selection requires a GameCube target.")return 3;
        }
        OutOfMemoryException? allocationError=null;
        try { var tooLarge=new byte[2*1024*1024];return tooLarge.Length; }
        catch(OutOfMemoryException error) {
            if(error.Message!="Managed heap exhausted.")return 4;
            allocationError=error;
        }
        if(!StorageApi.Mount(Dolphin.Storage.StorageDevice.SdSerialPort2))return 5;
        try {
            using var entries=System.IO.Directory.EnumerateFileSystemEntries("sd:/missing").GetEnumerator();
            entries.MoveNext();return 6;
        }catch(System.IO.DirectoryNotFoundException error) {
            if(error.Message!="Cannot open directory.")return 7;
        }
        // Native builtin errors are already singletons per kind. A different
        // cleanup error must preserve the earlier caught allocation diagnostic.
        if(allocationError.Message!="Managed heap exhausted.")return 8;
        try { System.IO.Path.GetFullPath("sd:/"+new string('a',1024));return 9; }
        catch(System.IO.PathTooLongException error) {
            if(error.Message!="Path length or filesystem directory depth limit exceeded.")return 10;
        }
        StorageApi.Unmount(Dolphin.Storage.StorageDevice.SdSerialPort2);
        return 0;
    }
}
