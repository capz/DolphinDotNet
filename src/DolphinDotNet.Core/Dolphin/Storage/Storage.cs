using System;
using System.Collections.Generic;
namespace Dolphin.Storage;
public enum StorageDevice { SdSlotA, SdSlotB, SdSerialPort2, DvdDrive, MemoryCardSlotA, MemoryCardSlotB, GcLoader }
public enum DiscFormat { Iso9660=1, GameCubeFst=2 }
public static class Storage
{
    public static bool Mount(StorageDevice device)=>NativeStorage.Mount((int)device);
    /// <summary>Mount a physical disc (or ODE-provided disc) at dvd:/.</summary>
    public static bool MountDisc(DiscFormat format)=>NativeStorage.MountDisc((int)format);
    /// <summary>Mount a plain GameCube ISO/GCM by full mounted-volume path as dvd:/.</summary>
    public static bool MountDisc(string imagePath)
    {
        if(imagePath==null)throw new ArgumentNullException("imagePath");
        return NativeStorage.MountDiscImage(imagePath);
    }
    /// <summary>Mount the currently loaded GameCube game, or an ISO at a full path.</summary>
    public static bool MountGameCubeDisc(string? imagePath=null)=>
        imagePath==null ? MountDisc(DiscFormat.GameCubeFst) : MountDisc(imagePath);
    /// <summary>Read the mounted GameCube boot header and optional opening.bnr
    /// artwork/text. Works for real media, ODE-loaded discs and ISO-backed dvd:/.
    /// Use the selected PAL language for BNR2; BNR1 has one language record.
    /// Throws if dvd:/ is unmounted or not a GameCube FST mount.</summary>
    public static DiscIdentity GetMountedDiscIdentity(
        DiscBannerLanguage language = DiscBannerLanguage.English)
    {
        if ((int)language < 0 || (int)language > 5)
            throw new ArgumentOutOfRangeException("language");
        byte[] header = NativeStorage.DiscHeader();
        DiscBanner? banner = null;
        if (FileExists(StorageDevice.DvdDrive, "opening.bnr"))
        {
            byte[] raw = ReadAllBytes(StorageDevice.DvdDrive, "opening.bnr");
            banner = DiscBanner.TryParse(raw, language);
        }
        return DiscIdentity.ParseHeader(header, banner);
    }
    public static bool IsMounted(StorageDevice device)=>NativeStorage.IsMounted((int)device);
    public static void Unmount(StorageDevice device)=>NativeStorage.Unmount((int)device);
    public static string GetPath(StorageDevice device,string path)=>NativeStorage.GetPath((int)device,path);
    public static bool FileExists(StorageDevice device,string path)=>IO.File.Exists(GetPath(device,path));
    public static bool DirectoryExists(StorageDevice device,string path)=>IO.Directory.Exists(GetPath(device,path));
    public static byte[] ReadAllBytes(StorageDevice device,string path)=>IO.File.ReadAllBytes(GetPath(device,path));
    public static void WriteAllBytes(StorageDevice device,string path,byte[] data)=>IO.File.WriteAllBytes(GetPath(device,path),data);
    public static IO.DirectoryInfo CreateDirectory(StorageDevice device,string path)=>IO.Directory.CreateDirectory(GetPath(device,path));
    public static IEnumerable<string> EnumerateFiles(StorageDevice device,string path)=>IO.Directory.EnumerateFiles(GetPath(device,path));
    public static IEnumerable<string> EnumerateDirectories(StorageDevice device,string path)=>IO.Directory.EnumerateDirectories(GetPath(device,path));
    public static string[] GetFiles(StorageDevice device,string path)=>IO.Directory.GetFiles(GetPath(device,path));
    public static string[] GetDirectories(StorageDevice device,string path)=>IO.Directory.GetDirectories(GetPath(device,path));
    public static IO.FileStream Open(StorageDevice device,string path,System.IO.FileMode mode,System.IO.FileAccess access,System.IO.FileShare share)=>IO.File.Open(GetPath(device,path),mode,access,share);
    public static void DeleteFile(StorageDevice device,string path)=>IO.File.Delete(GetPath(device,path));
    public static void DeleteDirectory(StorageDevice device,string path,bool recursive)=>IO.Directory.Delete(GetPath(device,path),recursive);
}
public sealed class MemoryCardIdentity
{
    public string GameCode {get;}
    public string CompanyCode {get;}
    public MemoryCardIdentity(string gameCode,string companyCode){GameCode=gameCode;CompanyCode=companyCode;}
}
public sealed class MemoryCardEntry
{
    public string Name {get;}
    public int Length {get;}
    internal MemoryCardEntry(string name,int length){Name=name;Length=length;}
}
public sealed class MemoryCardSaveOptions
{
    public string Title {get;set;}="";
    public string Comment {get;set;}="";
    /// <summary>Optional 96x32 tiled RGB5A3 image (6144 bytes).</summary>
    public byte[]? Banner {get;set;}
    /// <summary>Optional 32x32 tiled RGB5A3 image (2048 bytes).</summary>
    public byte[]? Icon {get;set;}
}
public sealed class MemoryCard : IDisposable
{
    private readonly StorageDevice device;
    private bool disposed;
    private MemoryCard(StorageDevice device){this.device=device;}
    public static MemoryCard Mount(StorageDevice device,MemoryCardIdentity identity)
    {
        if(identity==null)throw new ArgumentNullException("identity");
        if(!NativeStorage.CardMount((int)device,identity.GameCode,identity.CompanyCode))throw new System.IO.IOException("Memory card unavailable.");
        return new MemoryCard(device);
    }
    private void Check(){if(disposed)throw new ObjectDisposedException("MemoryCard");}
    public byte[] ReadAllBytes(string name){Check();return NativeStorage.CardRead((int)device,name);}
    public void WriteAllBytes(string name,byte[] data){Check();NativeStorage.CardWrite((int)device,name,data);}
    public void Delete(string name){Check();NativeStorage.CardDelete((int)device,name);}
    public void WriteSave(string name,byte[] data,MemoryCardSaveOptions options)
    {
        Check();if(options==null)throw new ArgumentNullException("options");
        NativeStorage.CardWriteSave((int)device,name,data,options.Title,options.Comment,options.Banner,options.Icon);
    }
    public MemoryCardEntry[] GetEntries()
    {
        Check();var names=NativeStorage.CardEntries((int)device);var entries=new MemoryCardEntry[names.Length];
        for(var i=0;i<names.Length;i++)entries[i]=new MemoryCardEntry(names[i],NativeStorage.CardLength((int)device,names[i]));
        return entries;
    }
    public void Dispose(){if(!disposed){NativeStorage.Unmount((int)device);disposed=true;}}
}
