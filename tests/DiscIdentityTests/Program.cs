using System;
using Dolphin.Storage;

internal static class Program
{
    private static void Assert(bool ok,string message)
    {
        if(!ok)throw new Exception(message);
    }
    private static void Put(byte[] bytes,int start,string s)
    {
        for(int i=0;i<s.Length;i++)bytes[start+i]=(byte)s[i];
    }
    private static byte[] Header()
    {
        var raw=new byte[DiscIdentity.HeaderSize];
        Put(raw,0,"GZLE01");
        raw[6]=1; raw[7]=3; raw[8]=1; raw[9]=9;
        raw[0x1c]=0xc2;raw[0x1d]=0x33;raw[0x1e]=0x9f;raw[0x1f]=0x3d;
        Put(raw,0x20,"Header Game Name");
        return raw;
    }
    private static byte[] Banner(bool pal)
    {
        var data=new byte[pal?0x1fa0:0x1960];
        Put(data,0,pal?"BNR2":"BNR1");
        // RGB5A3 tile pixels: first opaque red, second alpha pixel,
        // first pixel of the next 4x4 tile opaque green.
        data[0x20]=0xfc;data[0x21]=0x00;
        data[0x22]=0x71;data[0x23]=0x23;
        data[0x40]=0x83;data[0x41]=0xe0;
        int number=pal?6:1;
        for(int i=0;i<number;i++)
        {
            int p=0x1820+i*0x140;
            Put(data,p,"Short "+i);
            Put(data,p+32,"Maker "+i);
            Put(data,p+64,"Long game title "+i);
            Put(data,p+128,"Full maker "+i);
            Put(data,p+192,"Description "+i);
        }
        return data;
    }
    private static void ExpectError(Action action)
    {
        try {action();}
        catch(ArgumentException) {return;}
        throw new Exception("Expected argument validation");
    }
    private static void Main()
    {
        byte[] header=Header();
        var simple=DiscIdentity.ParseHeader(header);
        Assert(simple.GameId=="GZLE01","six character game ID");
        Assert(simple.GameCode=="GZLE" && simple.MakerCode=="01","ID split");
        Assert(simple.DiscNumber==1 && simple.Revision==3,"disc and revision");
        Assert(simple.AudioStreaming && simple.StreamBufferSize==9,"audio metadata");
        Assert(simple.Title=="Header Game Name" && simple.Banner==null,"fallback title");
        header[0]='X';
        Assert(simple.GameId=="GZLE01","immutable source header");
        byte[] copied=simple.RawHeader;copied[0]='X';
        Assert(simple.RawHeader[0]=='G',"defensive header copy");
        ExpectError(()=>DiscIdentity.ParseHeader(new byte[2]));
        ExpectError(()=>DiscIdentity.ParseHeader(new byte[DiscIdentity.HeaderSize]));
        Assert(DiscBanner.TryParse(new byte[10])==null,"truncated BNR");
        Assert(DiscBanner.TryParse(new byte[0x1960])==null,"invalid magic");
        byte[] bnr1=Banner(false);
        var banner=DiscBanner.TryParse(bnr1)!;
        Assert(banner!=null && !banner.IsMultilingual,"BNR1 recognized");
        Assert(banner.Title=="Long game title 0","BNR1 long title");
        Assert(banner.ShortTitle=="Short 0" && banner.Publisher=="Full maker 0","BNR1 strings");
        Assert(banner.Description=="Description 0","BNR1 description");
        Assert(banner.Rgb5A3.Length==6144 && banner.Rgba32.Length==96*32*4,"banner size");
        var rgba=banner.Rgba32;
        Assert(rgba[0]==255 && rgba[1]==0 && rgba[2]==0 && rgba[3]==255,"red RGB5A3");
        Assert(rgba[4]==17 && rgba[5]==34 && rgba[6]==51 && rgba[7]==238,"3-bit alpha RGBA");
        int x4=4*4;
        Assert(rgba[x4]==0 && rgba[x4+1]==255 && rgba[x4+2]==0,"GX tile order");
        bnr1[0x1820+64]=(byte)'X';
        rgba[0]=0;
        Assert(banner.Title=="Long game title 0" && banner.Rgba32[0]==255,"defensive banner snapshot");
        var merged=DiscIdentity.ParseHeader(Header(),banner);
        Assert(merged.Title=="Long game title 0" && merged.ShortDescription=="Description 0","identity composed");
        Assert(merged.GameId=="GZLE01","banner does not change header ID");
        byte[] bnr2=Banner(true);
        var dutch=DiscBanner.TryParse(bnr2,DiscBannerLanguage.Dutch)!;
        Assert(dutch!=null && dutch.IsMultilingual && dutch.Language==DiscBannerLanguage.Dutch,"BNR2 language");
        Assert(dutch.Title=="Long game title 5" && dutch.Description=="Description 5","Dutch record");
        Assert(DiscBanner.TryParse(bnr2,DiscBannerLanguage.French)!.Title=="Long game title 2","French record");
        Assert(DiscBanner.TryParse(new byte[0x1960])==null,"no fake BNR1 without magic");
        Put(bnr2,0,"BNR2");Assert(DiscBanner.TryParse(bnr2[..^1])==null,"truncated multilingual");
        Assert(DiscBanner.TryParse(bnr2,(DiscBannerLanguage)6)==null,"invalid language");
        Console.WriteLine("GameCube disc identity and BNR1/BNR2 banner parsing tests passed.");
    }
}
