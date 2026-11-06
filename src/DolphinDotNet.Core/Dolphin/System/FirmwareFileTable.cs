using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using static Dolphin.Formats.GameCubeBinary;

namespace Dolphin.System;

public sealed class FirmwareFile
{
    public string Path {get;}
    public int Offset {get;}
    public int Length {get;}
    internal FirmwareFile(string path,int offset,int length){Path=path;Offset=offset;Length=length;}
}

/// <summary>Strict inspection of an embedded GameCube FST layout. The table
/// owns metadata only, never the ROM. Unknown/reconstructed virtual filesystems
/// are not guessed from filename strings. All file offsets are ROM-relative.</summary>
public sealed class FirmwareFileTable
{
    private readonly FirmwareFile[] files;
    public int Offset {get;}
    public int Count=>files.Length;
    public FirmwareFile this[int index]=>files[index];
    private FirmwareFileTable(int offset,FirmwareFile[] entries){Offset=offset;files=entries;}
    public int Find(string path)
    {if(path==null)throw new ArgumentNullException(nameof(path));for(int i=0;i<Count;i++)if(files[i].Path==path)return i;return -1;}
    public byte[] Extract(byte[] image,int index)
    {
        if(index<0||index>=Count)throw new ArgumentOutOfRangeException(nameof(index));
        var entry=files[index];Check(image,entry.Offset,entry.Length);
        var result=new byte[entry.Length];Array.Copy(image,entry.Offset,result,0,result.Length);return result;
    }
    public static FirmwareFileTable? Inspect(byte[] image,CancellationToken cancellationToken=default)
    {
        if(image==null)throw new ArgumentNullException(nameof(image));
        if(image.Length!=FirmwareImage.ImageBytes)throw new InvalidDataException("Expected complete firmware image.");
        FirmwareFileTable? found=null;int attempts=0;
        for(int at=0x100;at<=0x1aff00-24;at+=4)
        {
            if((at&4095)==0)cancellationToken.ThrowIfCancellationRequested();
            if(U32(image,at)!=0x01000000||U32(image,at+4)!=0)continue;
            uint count=U32(image,at+8);
            if(count<3||count>4096||count>(0x1aff00-at)/12)continue;
            if(++attempts>128)throw new InvalidDataException("Firmware FST candidate budget exceeded.");
            FirmwareFileTable? candidate;
            try{candidate=Read(image,at,(int)count,cancellationToken);}
            catch(InvalidDataException){continue;}
            if(candidate==null)continue;
            if(found!=null)throw new InvalidDataException("Multiple valid firmware file tables.");
            found=candidate;
        }
        return found;
    }
    private static FirmwareFileTable? Read(byte[] d,int root,int count,CancellationToken cancellation)
    {
        int strings=root+count*12;
        if(d[strings]!=0)return null;
        var ends=new int[33];var parents=new int[33];var paths=new string[33];
        ends[0]=count;parents[0]=0;paths[0]="";int depth=0,tableEnd=strings+1,recognized=0;
        var result=new List<FirmwareFile>();
        for(int i=1;i<count;i++)
        {
            if((i&63)==0)cancellation.ThrowIfCancellationRequested();
            while(depth>0&&i>=ends[depth])depth--;
            int entry=root+i*12;
            uint tag=U32(d,entry);int kind=(int)(tag>>24),name=(int)(tag&0xffffff);
            if(kind>1||name==0||name>65535||strings>0x1aff00-name-1)
                throw new InvalidDataException("Invalid firmware FST entry.");
            int begin=strings+name,end=begin;
            while(end<0x1aff00&&end-begin<=255&&d[end]!=0)
            {if(d[end]<32||d[end]>126||d[end]=='/'||d[end]=='\\'||d[end]==':')throw new InvalidDataException("Invalid FST name.");end++;}
            if(end==begin||end>=0x1aff00||end-begin>255)throw new InvalidDataException("Unterminated firmware FST name.");
            string component=Ascii(d,begin,end-begin);
            if(component=="."||component=="..")throw new InvalidDataException("Invalid firmware FST traversal.");
            string path=paths[depth]+component;if(path.Length>512)throw new InvalidDataException("Firmware FST path exceeds budget.");
            tableEnd=Math.Max(tableEnd,end+1);
            if(kind==1)
            {
                int parent=AssetInt(U32(d,entry+4)),next=AssetInt(U32(d,entry+8));
                if(parent!=parents[depth]||next<=i||next>ends[depth]||depth==32)
                    throw new InvalidDataException("Invalid firmware FST hierarchy.");
                depth++;ends[depth]=next;parents[depth]=i;paths[depth]=path+"/";
            }
            else
            {
                int offset=AssetInt(U32(d,entry+4)),size=AssetInt(U32(d,entry+8));
                if(size<1||offset<0x100||offset>0x1aff00||size>0x1aff00-offset)
                    throw new InvalidDataException("Firmware file range outside resource area.");
                for(int j=0;j<result.Count;j++)
                    if(result[j].Path==path||offset<result[j].Offset+result[j].Length&&result[j].Offset<offset+size)
                        throw new InvalidDataException("Ambiguous firmware file paths or ranges.");
                if(FirmwareResource.Match(d,offset,"Yay0")||FirmwareResource.Match(d,offset,"WSYS")||FirmwareResource.Match(d,offset,"J3D1"))recognized++;
                result.Add(new FirmwareFile(path,offset,size));
            }
        }
        // Multiple structured files provide evidence beyond filenames/root magic.
        if(recognized<2||result.Count<2)return null;
        foreach(var file in result)if(file.Offset<tableEnd&&root<file.Offset+file.Length)
            throw new InvalidDataException("Firmware file overlaps FST metadata.");
        return new FirmwareFileTable(root,result.ToArray());
    }
}
