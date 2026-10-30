using System;
internal static class Program
{
    static void Must(bool ok) {if(!ok)throw new InvalidOperationException("Invalid compatibility fixture policy.");}
    private static void Big(byte[] data,int offset,uint value)
    {
        data[offset]=(byte)(value>>24);data[offset+1]=(byte)(value>>16);
        data[offset+2]=(byte)(value>>8);data[offset+3]=(byte)value;
    }
    private static void RarcFixtures()
    {
        var bytes=new byte[0xc0];
        bytes[0]=(byte)'R'; bytes[1]=(byte)'A'; bytes[2]=(byte)'R'; bytes[3]=(byte)'C';
        Big(bytes,4,0xc0); Big(bytes,8,0x20); Big(bytes,0xc,0x80);
        Big(bytes,0x20,1); Big(bytes,0x24,0x20);
        Big(bytes,0x28,1); Big(bytes,0x2c,0x30);
        Big(bytes,0x30,0x10); Big(bytes,0x34,0x44);
        bytes[0x4b]=1; /* root node holds one file entry */
        Big(bytes,0x58,0); Big(bytes,0x5c,0x20);
        var name="sample.bti";
        for(int i=0;i<name.Length;i++)bytes[0x64+i]=(byte)name[i];
        using var stream=new Dolphin.IO.MemoryStream(bytes,false);
        var corpus=new AssetCorpus();
        StreamingRarcIndex.Probe(stream,corpus,"gcloader:/games/test.iso","dvd:/res.arc");
        Must(corpus.Count==1 && corpus[0].Kind==AssetFixturePolicy.Texture);
        Must(corpus[0].Member=="sample.bti" && corpus[0].MemberOffset==0xa0);
        Must(corpus[0].MemberLength==32);
        bytes[0x5c]=0xff; /* out-of-bounds member is refused */
        using var broken=new Dolphin.IO.MemoryStream(bytes,false);
        var empty=new AssetCorpus();
        StreamingRarcIndex.Probe(broken,empty,"","dvd:/res.arc");
        Must(empty.Count==0);
    }
    static void Main()
    {
        Must(AssetFixturePolicy.DiscImage("gcloader:/games/Metroid.GCM"));
        Must(AssetFixturePolicy.DiscImage("games/Mario.ISO"));
        Must(!AssetFixturePolicy.DiscImage("games/rom.rvz"));
        Must(!AssetFixturePolicy.DiscImage("games/rom.nkit"));
        Must(AssetFixturePolicy.Kind("dvd:/model/player.bmd")==AssetFixturePolicy.Model);
        Must(AssetFixturePolicy.Kind("texture.BTI")==AssetFixturePolicy.Texture);
        Must(AssetFixturePolicy.Kind("effect.jpc")==AssetFixturePolicy.Particle);
        Must(AssetFixturePolicy.Kind("unknown.dat")==0);
        Must(AssetFixturePolicy.Archive("res/room.arc"));
        Must(!AssetFixturePolicy.Archive("res/room.dat"));
        byte[] model=new byte[0x20];model[0]=(byte)'J';model[1]=(byte)'3';
        model[2]=(byte)'D';model[3]=(byte)'2';
        Must(AssetFixturePolicy.ValidHeader(AssetFixturePolicy.Model,model));
        model[0]=0;
        Must(!AssetFixturePolicy.ValidHeader(AssetFixturePolicy.Model,model));
        var particle=new byte[0x20];
        var tag="JPAC2-10";
        for(int i=0;i<tag.Length;i++)particle[i]=(byte)tag[i];
        Must(AssetFixturePolicy.ValidHeader(AssetFixturePolicy.Particle,particle));
        Must(!AssetFixturePolicy.ValidHeader(AssetFixturePolicy.Texture,new byte[4]));
        Must(AssetFixturePolicy.MaxAssets<=32 && AssetFixturePolicy.MaxSourceBytes<=128*1024);
        RarcFixtures();
        Console.WriteLine("Hardware asset discovery and streaming RARC policy OK.");
    }
}
