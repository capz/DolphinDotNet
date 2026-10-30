using System;
using System.IO;
using Dolphin.Storage;
using DDirectory = Dolphin.IO.Directory;
using DFile = Dolphin.IO.File;
using DPath = Dolphin.IO.Path;

internal sealed class AssetCase
{
    public readonly string SourceImage, Path, Member;
    public readonly int Kind;
    public readonly long MemberOffset;
    public readonly int MemberLength;
    public AssetCase(string sourceImage,string path,string member,int kind,long offset=0,int length=0)
    {SourceImage=sourceImage;Path=path;Member=member;Kind=kind;MemberOffset=offset;MemberLength=length;}
    public string Name => (SourceImage.Length>0 ? DPath.GetFileName(SourceImage)+"::" : "")+
        Path+(Member.Length>0?"#"+Member:"");
}

internal sealed class AssetCorpus
{
    private readonly AssetCase[] entries=new AssetCase[AssetFixturePolicy.MaxAssets];
    private int count,seen,imageCount,archiveCount;
    public int Count=>count;
    public int Visited=>seen;
    public AssetCase this[int index]=>index>=0&&index<count?entries[index]:throw new ArgumentOutOfRangeException(nameof(index));

    public void Scan()
    {
        if(!Storage.Mount(StorageDevice.GcLoader) && !Storage.IsMounted(StorageDevice.GcLoader))
            throw new IOException("GC Loader SD filesystem is unavailable.");
        string games=Storage.GetPath(StorageDevice.GcLoader,"games");
        if(!DDirectory.Exists(games))throw new DirectoryNotFoundException("gcloader:/games is missing.");
        Walk(games,"",0);
    }

    private void Walk(string root,string sourceImage,int depth)
    {
        if(depth>AssetFixturePolicy.MaxNestedDepth||seen>=AssetFixturePolicy.MaxScannedEntries||
            count>=AssetFixturePolicy.MaxAssets)return;
        foreach(var file in DDirectory.EnumerateFiles(root))
        {
            if(++seen>AssetFixturePolicy.MaxScannedEntries||count>=AssetFixturePolicy.MaxAssets)break;
            if(sourceImage.Length==0&&AssetFixturePolicy.DiscImage(file))
            {
                if(imageCount++>=AssetFixturePolicy.MaxImages)continue;
                if(Storage.MountDisc(file))
                {
                    try{Walk("dvd:/",file,0);}
                    finally{Storage.Unmount(StorageDevice.DvdDrive);}
                }
                continue;
            }
            int kind=AssetFixturePolicy.Kind(file);
            if(kind!=0)
            {
                if(ValidFile(file,kind))Add(new AssetCase(sourceImage,file,"",kind));
            }
            else if(AssetFixturePolicy.Archive(file) && archiveCount<AssetFixturePolicy.MaxArchiveReads)
            {
                archiveCount++;
                TryArchive(file,sourceImage);
            }
        }
        if(depth>=AssetFixturePolicy.MaxNestedDepth)return;
        foreach(var dir in DDirectory.EnumerateDirectories(root))
        {
            if(seen>=AssetFixturePolicy.MaxScannedEntries||count>=AssetFixturePolicy.MaxAssets)break;
            Walk(dir,sourceImage,depth+1);
        }
    }

    private static bool ValidFile(string path,int kind)
    {
        try {
            using var file=DFile.OpenRead(path);
            if(file.Length<0x20||file.Length>(kind==AssetFixturePolicy.Model?AssetFixturePolicy.MaxModelBytes:AssetFixturePolicy.MaxSourceBytes))return false;
            var bytes=new byte[0x20];
            return file.Read(bytes,0,bytes.Length)==bytes.Length &&
                AssetFixturePolicy.ValidHeader(kind,bytes);
        }catch(IOException){return false;}
    }

    private void TryArchive(string path,string sourceImage)
    {
        try
        {
            using var stream=DFile.OpenRead(path);
            StreamingRarcIndex.Probe(stream,this,sourceImage,path);
        }
        catch(Exception error) when(error is InvalidDataException or NotSupportedException or IOException)
        {
            // Unsupported or corrupt archive metadata is not a testable member.
        }
    }

    internal void AddArchiveCandidate(string sourceImage,string archivePath,
        string member,int kind,long offset,int size)
        => Add(new AssetCase(sourceImage,archivePath,member,kind,offset,size));

    private void Add(AssetCase candidate)
    {
        if(count>=entries.Length || candidate.Name.Length>215)return;
        // Stable case ordering for a fixed candidate set, independent of VFAT iteration.
        int at=count;
        while(at>0 && string.CompareOrdinal(candidate.Name,entries[at-1].Name)<0)
        {entries[at]=entries[at-1];at--;}
        entries[at]=candidate;count++;
    }

    public static byte[] ReadBounded(string path)
    {
        using var stream=DFile.OpenRead(path);
        if(stream.Length<0x20||stream.Length>AssetFixturePolicy.MaxSourceBytes)
            throw new NotSupportedException("Asset size is outside the bounded hardware fixture profile.");
        var data=new byte[(int)stream.Length];
        int done=0;
        while(done<data.Length)
        {
            int n=stream.Read(data,done,data.Length-done);
            if(n<=0)throw new EndOfStreamException("Unexpected end of mounted asset.");
            done+=n;
        }
        return data;
    }

    public static byte[] ReadAsset(AssetCase entry)
    {
        if(entry.Member.Length==0)return ReadBounded(entry.Path);
        if(entry.MemberLength<0x20 || entry.MemberLength>AssetFixturePolicy.MaxSourceBytes)
            throw new NotSupportedException("Unbounded RARC member.");
        using var stream=DFile.OpenRead(entry.Path);
        if(entry.MemberOffset<0 || entry.MemberOffset>stream.Length-entry.MemberLength)
            throw new InvalidDataException("RARC member exceeds archive.");
        stream.Seek(entry.MemberOffset,SeekOrigin.Begin);
        var bytes=new byte[entry.MemberLength];
        int offset=0;
        while(offset<bytes.Length)
        {
            int count=stream.Read(bytes,offset,bytes.Length-offset);
            if(count<=0)throw new EndOfStreamException("Short RARC member.");
            offset+=count;
        }
        return bytes;
    }
}
