using System;
using System.IO;
using System.Text;
using DStream = Dolphin.IO.Stream;

/// <summary>
/// Read-only, allocation-bounded RARC metadata walker over a seekable stream.
/// Unlike RarcArchive.Read this never loads the whole retail archive, which
/// would exceed the GameCube AOT managed heap. No Yaz0 decompression here.
/// </summary>
internal sealed class StreamingRarcIndex
{
    private readonly DStream stream;
    private readonly AssetCorpus corpus;
    private readonly string sourceImage, archivePath;
    private readonly long dataBase,nodeBase,entryBase,stringBase,stringLength;
    private readonly int nodeCount,entryCount;
    private readonly bool[] visiting;
    private readonly byte[] header=new byte[0x40], node=new byte[0x10],
        entry=new byte[0x14],name=new byte[128],preview=new byte[0x20];
    private int considered;
    private StreamingRarcIndex(DStream stream,AssetCorpus corpus,string image,string path)
    {
        this.stream=stream;this.corpus=corpus;sourceImage=image;archivePath=path;
        ReadExact(0,header,0,header.Length);
        if(header[0]!='R'||header[1]!='A'||header[2]!='R'||header[3]!='C'||
           U32(header,8)!=0x20)
            throw new NotSupportedException("Compressed/non-RARC member index is not streamed.");
        long length=U32(header,4);
        if(length<0x40 || length>stream.Length)
            throw new InvalidDataException("RARC declared length invalid.");
        dataBase=0x20L+U32(header,0xC);
        nodeCount=(int)U32(header,0x20);
        nodeBase=0x20L+U32(header,0x24);
        entryCount=(int)U32(header,0x28);
        entryBase=0x20L+U32(header,0x2c);
        stringLength=U32(header,0x30);
        stringBase=0x20L+U32(header,0x34);
        if(nodeCount<1 || nodeCount>8192 || entryCount<0 || entryCount>65535 ||
           stringLength>4194304L || !Range(nodeBase,(long)nodeCount*16) ||
           !Range(entryBase,(long)entryCount*20) || !Range(stringBase,stringLength) ||
           !Range(dataBase,0))
            throw new InvalidDataException("Invalid RARC directory/index bounds.");
        visiting=new bool[nodeCount];
    }
    private bool Range(long offset,long size)
        => offset>=0&&size>=0&&offset<=stream.Length&&size<=stream.Length-offset;
    private void ReadExact(long offset,byte[] data,int start,int length)
    {
        if(!Range(offset,length))throw new InvalidDataException("RARC stream read exceeds file.");
        stream.Seek(offset,SeekOrigin.Begin);
        int end=start+length;
        while(start<end)
        {
            int n=stream.Read(data,start,end-start);
            if(n<=0)throw new EndOfStreamException("Short RARC metadata read.");
            start+=n;
        }
    }
    private static uint U32(byte[] bytes,int offset)=>
        ((uint)bytes[offset]<<24)|((uint)bytes[offset+1]<<16)|
        ((uint)bytes[offset+2]<<8)|bytes[offset+3];
    private static int U16(byte[] bytes,int offset)=>(bytes[offset]<<8)|bytes[offset+1];
    private string Name(uint offset)
    {
        if(offset>=stringLength)throw new InvalidDataException("Invalid RARC name reference.");
        int count=(int)Math.Min(128,stringLength-offset);
        ReadExact(stringBase+offset,name,0,count);
        int n=0;
        while(n<count&&name[n]!=0)n++;
        if(n==count)throw new InvalidDataException("Unterminated RARC entry name.");
        for(int i=0;i<n;i++)if(name[i]=='/'||name[i]=='\\'||name[i]==':'||
           name[i]<32||name[i]>126)
           throw new NotSupportedException("Non-ASCII or unsafe RARC entry name.");
        return Encoding.ASCII.GetString(name,0,n);
    }
    private void Visit(int id,string prefix,int depth)
    {
        if(depth>6||id<0||id>=nodeCount||visiting[id])
            throw new InvalidDataException("RARC directory cycle or excessive depth.");
        visiting[id]=true;
        ReadExact(nodeBase+(long)id*16,node,0,node.Length);
        int count=U16(node,0xA);
        long first=U32(node,0xC);
        if(first+count>entryCount)throw new InvalidDataException("Invalid RARC node entries.");
        for(int i=0;i<count && considered<512 && corpus.Count<AssetFixturePolicy.MaxAssets;i++)
        {
            ReadExact(entryBase+(first+i)*20,entry,0,entry.Length);
            uint flagsAndName=U32(entry,4);
            uint childOrData=U32(entry,8);
            string name=Name(flagsAndName&0xffffff);
            if(name=="."||name==".."||name.Length==0)continue;
            string member=prefix.Length==0?name:prefix+"/"+name;
            if((flagsAndName & 0x02000000u)!=0)
            {
                Visit((int)childOrData,member,depth+1);
            }
            else
            {
                considered++;
                int kind=AssetFixturePolicy.Kind(member);
                uint size=U32(entry,0xc);
                long offset=dataBase+childOrData;
                if(kind==0 || size<32 ||
                   size>(kind==AssetFixturePolicy.Model?
                       AssetFixturePolicy.MaxModelBytes:AssetFixturePolicy.MaxSourceBytes) ||
                   !Range(offset,size))continue;
                ReadExact(offset,preview,0,preview.Length);
                if(AssetFixturePolicy.ValidHeader(kind,preview))
                    corpus.AddArchiveCandidate(sourceImage,archivePath,member,kind,offset,(int)size);
            }
        }
        visiting[id]=false;
    }
    public static void Probe(DStream stream,AssetCorpus corpus,string sourceImage,string archivePath)
    {
        if(stream==null||!stream.CanRead||!stream.CanSeek)
            throw new ArgumentException("RARC stream must be seekable.");
        var index=new StreamingRarcIndex(stream,corpus,sourceImage,archivePath);
        index.Visit(0,"",0);
    }
}
