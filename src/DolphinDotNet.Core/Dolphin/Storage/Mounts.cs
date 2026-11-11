using System;
using System.IO;
namespace Dolphin.Storage;

public enum MountKind { BlockVolume=1, Disc=2, DiscImage=3 }
public enum FileSystemFormat { Automatic=0, Fat=1, ExFat=2 }
public enum MountState { NeverMounted=0, Mounted=1, Unmounted=2, Unavailable=3 }
public enum MountPresence { Absent=0, Present=1, Busy=2, Unknown=3, Unsupported=4 }
public enum MountFailure { None=0, NoPreviousConfiguration=1, Unavailable=2, Busy=3, ConfigurationConflict=4, Unsupported=5, InvalidConfiguration=6, IoError=7 }
public enum MountChangeReason { Mounted=1, Unmounted=2, Removed=3, HistoryLost=4 }

/// <summary>Immutable, reusable mount recipe. A successful snapshot contains
/// effective partition/filesystem/cache settings, rather than just auto-selection.</summary>
public sealed class MountConfiguration
{
    public StorageDevice Device {get;}
    public MountKind Kind {get;}
    public DiscFormat DiscFormat {get;}
    public FileSystemFormat FileSystem {get;}
    public long PartitionLba {get;}
    public int CachePages {get;}
    public int SectorsPerPage {get;}
    public string ImagePath {get;}
    public MountConfiguration? SourceConfiguration {get;}
    internal MountConfiguration(StorageDevice device,MountKind kind,DiscFormat format,
        FileSystemFormat fileSystem,long partition,int pages,int sectors,string image,MountConfiguration? source)
    {Device=device;Kind=kind;DiscFormat=format;FileSystem=fileSystem;PartitionLba=partition;
     CachePages=pages;SectorsPerPage=sectors;ImagePath=image;SourceConfiguration=source;}
    public static MountConfiguration BlockVolume(StorageDevice device,long partitionLba=-1,
        FileSystemFormat fileSystem=FileSystemFormat.Automatic,int cachePages=4,int sectorsPerPage=8)
    {
        Storage.ValidateFileSystemDevice(device);
        if(device==StorageDevice.DvdDrive)throw new ArgumentException("Use Disc or DiscImage for dvd:/.");
        if(partitionLba< -1||(int)fileSystem<0||(int)fileSystem>2||cachePages<1||cachePages>64||
            (cachePages&(cachePages-1))!=0||sectorsPerPage<1||sectorsPerPage>128||(sectorsPerPage&(sectorsPerPage-1))!=0)
            throw new ArgumentOutOfRangeException("configuration");
        return new MountConfiguration(device,MountKind.BlockVolume,0,fileSystem,partitionLba,cachePages,sectorsPerPage,"",null);
    }
    public static MountConfiguration Disc(DiscFormat format=DiscFormat.Fst)
    {
        if(format!=DiscFormat.Fst&&format!=DiscFormat.Iso9660)throw new ArgumentOutOfRangeException("format");
        return new MountConfiguration(StorageDevice.DvdDrive,MountKind.Disc,format,0,-1,0,0,"",null);
    }
    public static MountConfiguration DiscImage(string imagePath,MountConfiguration? source=null)
    {
        if(imagePath==null)throw new ArgumentNullException("imagePath");
        if(imagePath.Length==0||imagePath.IndexOf(":/")<=0||imagePath.IndexOf('\0')>=0)
            throw new ArgumentException("Use a full mounted-volume image path.");
        if(source!=null&&source.Kind!=MountKind.BlockVolume)throw new ArgumentException("An image requires a block-volume source.");
        return new MountConfiguration(StorageDevice.DvdDrive,MountKind.DiscImage,DiscFormat.Fst,0,-1,0,0,imagePath,source);
    }
}

/// <summary>Immutable observation; queries do not probe or mount hardware.</summary>
public sealed class MountInfo
{
    public StorageDevice Device {get;}
    public MountState State {get;}
    public MountPresence Presence {get;}
    public bool IsMounted=>State==MountState.Mounted;
    public bool IsReadOnly=>Device==StorageDevice.DvdDrive;
    public string RootPath=>Storage.Root(Device);
    public MountConfiguration? LastSuccessfulConfiguration {get;}
    public long MountId {get;}
    public long LastMountSequence {get;}
    public long ChangeSequence {get;}
    public long SourceMountId {get;}
    /// <summary>Backend errno from the last failed attempt, or zero after success.</summary>
    public int LastNativeError {get;}
    internal MountInfo(byte[] data)
    {
        Device=(StorageDevice)Field(data,2);State=(MountState)Field(data,1);
        MountId=Field(data,11);LastMountSequence=Field(data,12);ChangeSequence=Field(data,13);
        LastNativeError=(int)Field(data,14);Presence=(MountPresence)Field(data,15);SourceMountId=Field(data,10);
        int kind=(int)Field(data,3);
        if(kind!=0) {
            int source=(int)Field(data,9);MountConfiguration? dependency=null;
            if(kind==3&&source>=0)dependency=new MountConfiguration((StorageDevice)source,MountKind.BlockVolume,0,
                (FileSystemFormat)Field(data,18),Field(data,17),(int)Field(data,19),(int)Field(data,20),"",null);
            string image=data.Length==168?"":Dolphin.Text.Encoding.UTF8.GetString(data,168,data.Length-168);
            LastSuccessfulConfiguration=new MountConfiguration(Device,(MountKind)kind,(DiscFormat)Field(data,4),
                (FileSystemFormat)Field(data,5),Field(data,6),(int)Field(data,7),(int)Field(data,8),image,dependency);
        }
    }
    internal static long Field(byte[] data,int field)
    {
        if(data.Length<168||data[0]!=1)throw new InvalidOperationException("Unsupported native mount snapshot.");
        ulong value=0;for(int i=0;i<8;i++)value|=(ulong)data[field*8+i]<<(i*8);
        return unchecked((long)value);
    }
}

/// <summary>Borrowed access to a single mount generation. Does not own or unmount it.</summary>
public sealed class MountedFileSystem
{
    public MountInfo Info {get;}
    public StorageDevice Device=>Info.Device;
    public long MountId=>Info.MountId;
    public string RootPath=>Info.RootPath;
    public bool IsValid=>NativeStorage.MountValid((int)Device,MountId);
    internal MountedFileSystem(MountInfo info){Info=info;}
    /// <summary>The returned string follows the current path namespace. Use Open
    /// when access must remain tied to this particular mount lifetime.</summary>
    public string GetPath(string relativePath)
    {
        if(!IsValid)throw new DriveNotFoundException("The filesystem mount is no longer active.");
        return Storage.GetPath(Device,relativePath);
    }
    public Dolphin.IO.FileStream Open(string relativePath,FileMode mode,FileAccess access,FileShare share=FileShare.None)
        =>new Dolphin.IO.FileStream(NativeStorage.OpenMounted((int)Device,MountId,relativePath,(int)mode,(int)access,(int)share),access);
}
public sealed class MountResult
{
    public MountFailure Failure {get;}
    public bool Succeeded=>Failure==MountFailure.None;
    public MountedFileSystem? FileSystem {get;}
    internal MountResult(MountFailure failure,MountedFileSystem? fileSystem){Failure=failure;FileSystem=fileSystem;}
}
public sealed class MountChange
{
    public MountChangeReason Reason {get;}
    /// <summary>Null for HistoryLost; call GetMounts to resynchronize.</summary>
    public MountInfo? Info {get;}
    public long Sequence {get;}
    internal MountChange(byte[] data)
    {
        Reason=(MountChangeReason)MountInfo.Field(data,16);Sequence=MountInfo.Field(data,13);
        if(Reason!=MountChangeReason.HistoryLost)Info=new MountInfo(data);
    }
}
public static partial class Storage
{
    private static bool polling;
    // Copy-on-write subscriptions also work on AOT, which does not expose
    // System.Delegate.Combine/Remove. Dispatch observes a stable subscriber list.
    private static Action<MountChange>[] mountHandlers=new Action<MountChange>[0];
    public static event Action<MountChange> MountChanged
    {
        add {
            if(ReferenceEquals(value,null))return;
            var old=mountHandlers;var next=new Action<MountChange>[old.Length+1];
            Array.Copy(old,next,old.Length);next[old.Length]=value;mountHandlers=next;
        }
        remove {
            var old=mountHandlers;
            for(int i=old.Length-1;i>=0;i--)if(NativeStorage.MountHandlerEquals(old[i],value)) {
                var next=new Action<MountChange>[old.Length-1];
                Array.Copy(old,0,next,0,i);Array.Copy(old,i+1,next,i,old.Length-i-1);
                mountHandlers=next;return;
            }
        }
    }
    internal static void ValidateFileSystemDevice(StorageDevice device)
    {
        int d=(int)device;
        if(d<0||d>6||d==4||d==5)throw new ArgumentException("Not a filesystem device.","device");
    }
    internal static string Root(StorageDevice device)
    {
        ValidateFileSystemDevice(device);
        return device==StorageDevice.SdSlotA?"carda:/":device==StorageDevice.SdSlotB?"cardb:/":
            device==StorageDevice.SdSerialPort2?"sd:/":device==StorageDevice.DvdDrive?"dvd:/":"gcloader:/";
    }
    public static MountInfo GetMountInfo(StorageDevice device)
    {ValidateFileSystemDevice(device);return new MountInfo(NativeStorage.MountSnapshot((int)device));}
    public static MountInfo[] GetMounts()=>new[]{GetMountInfo(StorageDevice.SdSlotA),GetMountInfo(StorageDevice.SdSlotB),
        GetMountInfo(StorageDevice.SdSerialPort2),GetMountInfo(StorageDevice.DvdDrive),GetMountInfo(StorageDevice.GcLoader)};
    /// <summary>Most recent successful mount, including one since unmounted.
    /// Failed attempts, idempotent mounts and unmounts do not reorder it.</summary>
    public static MountInfo? GetLastMount()
    {int d=NativeStorage.LastMountDevice();return d<0?null:GetMountInfo((StorageDevice)d);}
    public static bool TryGetMountedFileSystem(StorageDevice device,out MountedFileSystem? fileSystem)
    {
        var info=GetMountInfo(device);fileSystem=info.IsMounted?new MountedFileSystem(info):null;return fileSystem!=null;
    }
    private static MountResult MountOutcome(StorageDevice device,int result)
    {
        MountedFileSystem? fileSystem=null;
        if(result==0&&!TryGetMountedFileSystem(device,out fileSystem))result=(int)MountFailure.Unavailable;
        return new MountResult((MountFailure)result,fileSystem);
    }
    public static MountResult Mount(MountConfiguration configuration)
    {
        if(configuration==null)throw new ArgumentNullException("configuration");
        var c=configuration;var s=c.SourceConfiguration;
        return MountOutcome(c.Device,NativeStorage.MountConfigured((int)c.Device,(int)c.Kind,(int)c.DiscFormat,c.ImagePath,
            c.PartitionLba,(int)c.FileSystem,c.CachePages,c.SectorsPerPage,s==null?-1:(int)s.Device,
            s==null?-1:s.PartitionLba,s==null?0:(int)s.FileSystem,s==null?0:s.CachePages,s==null?0:s.SectorsPerPage));
    }
    public static MountResult Remount(StorageDevice device)
    {ValidateFileSystemDevice(device);return MountOutcome(device,NativeStorage.Remount((int)device));}
    /// <summary>Observe removal and deliver at most 17 queued changes on the
    /// calling application thread. Call once per update; no background callbacks.
    /// Exceptions from a subscriber propagate after the mount transition commits.</summary>
    public static void Poll()
    {
        if(polling)return;polling=true;
        try {
            NativeStorage.PollMounts();
            for(int i=0;i<17;i++) {
                byte[]? data=NativeStorage.NextMountChange();if(data==null)break;
                var handlers=mountHandlers;
                if(handlers.Length!=0) {
                    var change=new MountChange(data);
                    for(int n=0;n<handlers.Length;n++)handlers[n](change);
                }
            }
        }finally{polling=false;}
    }
}
