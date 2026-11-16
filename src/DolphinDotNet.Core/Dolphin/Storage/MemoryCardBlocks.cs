using System;
using System.IO;
namespace Dolphin.Storage;

/// <summary>Block backend for CardRecordStore; retain the owning MemoryCard
/// mount and exclusively own this file for the backend's lifetime.</summary>
public sealed class MemoryCardBlocks : ICardBlockStorage
{
    private readonly int device,id;
    private readonly string name;
    public int Blocks {get;}
    private static readonly byte[] Empty=new byte[0];
    internal MemoryCardBlocks(int device,string name,int blocks,MemoryCardSaveOptions options,int identity)
    {
        this.device=device;this.name=name;
        int size=Check(NativeStorage.CardBlocksIo(device,name,0,0,0,Empty));
        if(size==0){
            if(blocks<6||blocks>256||identity<=0)throw new ArgumentOutOfRangeException("blocks/identity");
            if(options.Banner==null||options.Banner.Length!=6144||options.Icon==null||options.Icon.Length!=2048)throw new ArgumentException("Record store requires banner and icon");
            var header=new byte[16384];Text(header,32,options.Title);Text(header,64,options.Comment);
            Array.Copy(options.Banner,0,header,96,6144);Array.Copy(options.Icon,0,header,6240,2048);
            Check(NativeStorage.CardBlocksIo(device,name,3,blocks,identity,header));size=blocks;
        }
        Blocks=size;id=Check(NativeStorage.CardBlocksIo(device,name,4,0,0,Empty));
    }
    private static void Text(byte[] bytes,int at,string text)
    {if(text==null||text.Length>31)throw new ArgumentException("Card title/comment must fit 31 ASCII bytes");for(int i=0;i<text.Length;i++){if(text[i]<32||text[i]>126)throw new ArgumentException("Card title/comment must be ASCII");bytes[at+i]=(byte)text[i];}}
    private static int Check(int n){if(n<0)throw new IOException("Card block operation failed: "+(-n));return n;}
    public void Read(int block,byte[] bytes){Check(NativeStorage.CardBlocksIo(device,name,1,block,id,bytes));}
    public void Write(int block,byte[] bytes){Check(NativeStorage.CardBlocksIo(device,name,2,block,id,bytes));}
}
