using System;
internal static class Program
{
    static void Must(bool ok) {if(!ok)throw new InvalidOperationException("Invalid compatibility fixture policy.");}
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
        Console.WriteLine("Hardware asset discovery policy OK.");
    }
}
