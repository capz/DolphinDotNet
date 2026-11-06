using System;
using System.IO;
namespace Dolphin.Storage;

/// <summary>The requested mutation committed; only the subsequent directory refresh failed.
/// Reopen/refresh before retrying. Do not repeat the destructive action.</summary>
public sealed class MemoryCardCommittedException : IOException
{
    internal MemoryCardCommittedException():base("Operation completed, but the card directory could not be refreshed. Reopen the slot."){}
}

/// <summary>A save snapshot. Any refresh or card removal invalidates its native handle.
/// Raw names and ownership are preserved by native operations, including Japanese bytes.</summary>
public sealed class ManagedCardSave
{
    internal readonly int Owner,Revision,Index;
    private readonly byte[] metadata;
    internal ManagedCardSave(int owner,int revision,int index,byte[] data){Owner=owner;Revision=revision;Index=index;metadata=data;}
    private int Number(int at)=>(int)((uint)metadata[at]<<24|(uint)metadata[at+1]<<16|(uint)metadata[at+2]<<8|metadata[at+3]);
    private string Text(int at,int length){int count=0;while(count<length&&metadata[at+count]!=0)count++;return Dolphin.Text.Encoding.GetEncoding(28591).GetString(metadata,at,count);}
    public int Length=>Number(0);
    public int Blocks=>Length/8192;
    public uint ModifiedSecondsSince2000=>(uint)Number(4);
    public int Attributes=>metadata[8];
    public bool CanCopy=>(Attributes&8)==0;
    public bool CanMove=>(Attributes&16)==0;
    /// <summary>Latin-1 byte-preserving view; no Shift-JIS-to-Unicode conversion is implied.</summary>
    public string Filename=>Text(24,32);
    public string GameCode=>Text(16,4);
    public string CompanyCode=>Text(20,2);
    public string Title=>Text(56,32);
    public string Comment=>Text(88,32);
    public byte[] RawFilename {get{var name=new byte[32];Array.Copy(metadata,24,name,0,32);return name;}}
    public bool IconAnimationBounces=>(metadata[9]&4)!=0;
    public int IconFrameCount {get{int count=0;int formats=metadata[22]<<8|metadata[23];for(int i=0;i<8;i++)if(((formats>>(i*2))&3)!=0)count=i+1;return count;}}
    public int IconFrameDuration(int frame){if(frame<0||frame>7)throw new ArgumentOutOfRangeException("frame");return ((metadata[10]<<8|metadata[11])>>(2*frame)&3)*4;}
}

/// <summary>Unfiltered, bounded IPL-style card management on the application executor.
/// Exclusively owns one slot until disposed; cannot coexist with game-scoped mounts.</summary>
public sealed class MemoryCardManager : IDisposable
{
    private int token,revision;
    public StorageDevice Device {get;}
    private MemoryCardManager(StorageDevice device,int handle){Device=device;token=handle;revision=Check(NativeStorage.CardManagerStat(token,4));}
    public static MemoryCardManager Open(StorageDevice device)
    {
        if(device!=StorageDevice.MemoryCardSlotA&&device!=StorageDevice.MemoryCardSlotB)throw new ArgumentOutOfRangeException("device");
        if(!Dolphin.Threading.Tasks.Scheduler.IsApplicationThread)throw new InvalidOperationException("Manage memory cards on the application executor.");
        int handle=Check(NativeStorage.CardManagerOpen(device==StorageDevice.MemoryCardSlotA?0:1));
        try{return new MemoryCardManager(device,handle);}catch{NativeStorage.CardManagerClose(handle);throw;}
    }
    internal void Alive(){if(token==0)throw new ObjectDisposedException("MemoryCardManager");if(!Dolphin.Threading.Tasks.Scheduler.IsApplicationThread)throw new InvalidOperationException("Manage memory cards on the application executor.");}
    /// <summary>False for an unformatted/broken or differently encoded card. No automatic formatting occurs.</summary>
    public bool IsReady {get{Alive();int status=NativeStorage.CardManagerStat(token,0);if(status<=-1000)Check(status);return status>=0;}}
    public int MountResult {get{Alive();return NativeStorage.CardManagerStat(token,0);}}
    public int Count {get{Alive();return Check(NativeStorage.CardManagerStat(token,1));}}
    public int FreeBlocks {get{Alive();return Check(NativeStorage.CardManagerStat(token,2));}}
    public int TotalBlocks {get{Alive();return Check(NativeStorage.CardManagerStat(token,3));}}
    public int FreeEntries {get{Alive();return Check(NativeStorage.CardManagerStat(token,5));}}
    public void Refresh(){Alive();Check(NativeStorage.CardManagerRefresh(token));revision=Check(NativeStorage.CardManagerStat(token,4));}
    public ManagedCardSave[] ReadPage(int start,int maximum=6)
    {
        Alive();if(start<0||maximum<1||maximum>16)throw new ArgumentOutOfRangeException("start");int count=Math.Max(0,Math.Min(maximum,Count-start));var page=new ManagedCardSave[count];
        for(int i=0;i<count;i++){var data=new byte[128];Check(NativeStorage.CardManagerEntry(token,revision,start+i,data));page[i]=new ManagedCardSave(token,revision,start+i,data);}return page;
    }
    private void Own(ManagedCardSave save){Alive();if(save==null)throw new ArgumentNullException("save");if(save.Owner!=token||save.Revision!=revision)throw new InvalidOperationException("Save snapshot changed. Refresh and select it again.");}
    /// <summary>One 32×32 RGBA8 icon frame; no icon returns transparent pixels. Caller owns the 4 KiB array.</summary>
    public byte[] ReadIcon(ManagedCardSave save,int frame=0){Own(save);if(frame<0||frame>7)throw new ArgumentOutOfRangeException("frame");var rgba=new byte[4096];Check(NativeStorage.CardManagerIcon(token,revision,save.Index,frame,rgba));return rgba;}
    /// <summary>Deletes this save. The caller must obtain explicit user confirmation.</summary>
    public void Delete(ManagedCardSave save){Own(save);Check(NativeStorage.CardManagerDelete(token,revision,save.Index));revision=Check(NativeStorage.CardManagerStat(token,4));}
    /// <summary>Erases every save. The caller must show an explicit formatting confirmation.</summary>
    public void Format(){Alive();Check(NativeStorage.CardManagerFormat(token));revision=Check(NativeStorage.CardManagerStat(token,4));}
    public MemoryCardTransfer BeginCopy(ManagedCardSave save,MemoryCardManager destination,bool move=false)
    {
        Own(save);if(destination==null)throw new ArgumentNullException("destination");destination.Alive();int operation=Check(NativeStorage.CardManagerCopyBegin(token,revision,save.Index,destination.token,move?1:0));
        try{return new MemoryCardTransfer(operation,this,destination);}catch{NativeStorage.CardManagerCopyAbort(operation);throw;}
    }
    internal void SyncRevision(){Alive();revision=Check(NativeStorage.CardManagerStat(token,4));}
    public void Dispose(){if(token!=0){Alive();NativeStorage.CardManagerClose(token);token=0;}}
    internal static int Check(int result)
    {
        if(result>=0)return result;
        if(result==-1001)throw new InvalidOperationException("Card or save changed. Reopen the slot and select again.");
        if(result==-1002)throw new IOException("Memory card is not ready. Select its slot and check its status.");
        if(result==-1003)throw new IOException("This save cannot be copied.");
        if(result==-1004)throw new IOException("This save cannot be moved.");
        if(result==-1005)throw new IOException("Card data or copied bytes could not be verified. Source retained.");
        if(result==-1006)throw new InvalidOperationException("Card slot or transfer is already in use.");
        if(result==-1007)throw new IOException("Source retained. A partial destination could not be safely removed; inspect the destination card.");
        if(result==-1008)throw new MemoryCardCommittedException();
        if(result==-2001)throw new OutOfMemoryException();
        if(result==-2002)throw new PlatformNotSupportedException("Card management requires a GameCube native build.");
        if(result==-8)throw new IOException("Memory card has no free directory entries.");
        if(result==-9)throw new IOException("Memory card has no free blocks.");
        if(result==-7)throw new IOException("A save with the same name and game identity already exists. Nothing was overwritten.");
        if(result==-3)throw new IOException("No memory card in this slot.");
        throw new IOException("Memory card operation failed ("+result.ToString()+").");
    }
}

/// <summary>Copy/readback verification in 8 KiB steps. Source of a move is removed
/// only after full data CRC and metadata verification. Dispose cancels a partial copy.</summary>
public sealed class MemoryCardTransfer : IDisposable
{
    private int token;private readonly MemoryCardManager source,destination;
    public int Progress {get;private set;}
    public bool Complete {get;private set;}
    internal MemoryCardTransfer(int token,MemoryCardManager source,MemoryCardManager destination){this.token=token;this.source=source;this.destination=destination;}
    public void Step(){source.Alive();destination.Alive();if(token==0)throw new ObjectDisposedException("MemoryCardTransfer");int result=NativeStorage.CardManagerCopyStep(token);if(result<0){token=0;if(result==-1008){Complete=true;Progress=100;}MemoryCardManager.Check(result);}Progress=result;if(result==100){token=0;Complete=true;source.SyncRevision();destination.SyncRevision();}}
    public void Cancel(){if(token!=0){source.Alive();destination.Alive();int handle=token;token=0;MemoryCardManager.Check(NativeStorage.CardManagerCopyAbort(handle));}}
    public void Dispose(){Cancel();}
}
