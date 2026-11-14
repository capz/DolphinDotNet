using System;
using StorageApi=Dolphin.Storage.Storage;

internal static class Program
{
    public static int Main()
    {
        try { StorageApi.SelectGcLoaderDiscImage(null!);return 1; }
        catch(ArgumentNullException) {}
        try { StorageApi.SelectGcLoaderDiscImage("gcloader:/new.iso");return 2; }
        catch(NotSupportedException) {}
        return 0;
    }
}
