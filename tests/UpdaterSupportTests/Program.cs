using System;
using Dolphin.IO;
using Dolphin.Network.Http;
class Program
{
    static void Main()
    {
        string[] source={"gcloader:/games/AnimalTown.iso","carda:/game.nkit.iso","cardb:/game.gcm","sd:/game.ISO"};
        string[] expected={"gcldr:/games/AnimalTown.iso","sda:/game.nkit.iso","sdb:/game.gcm","sdc:/game.ISO"};
        for(int i=0;i<source.Length;i++)if(DiscLauncher.GetSwissImagePath(source[i])!=expected[i])throw new Exception("Swiss path mapping");
        foreach(string bad in new[]{"dvd:/game.iso","sd:/../game.iso","sd:/a/./game.iso","sd:/a//game.iso","sd:/game.iso\nAutoload=x","sd:/game=foo.iso","sd:/game.dol","sd:/","sd:/x"}){
            bool rejected=false;try{DiscLauncher.GetSwissImagePath(bad);}catch(ArgumentException){rejected=true;}if(!rejected)throw new Exception("Unsafe Swiss path: "+bad);
        }
        using var client=new HttpClient();if(client.MaximumStreamBytes!=16777216||client.MaximumResponseBytes!=65536)throw new Exception("HTTP default limits changed");
        client.MaximumStreamBytes=int.MaxValue;if(client.MaximumResponseBytes!=65536)throw new Exception("Streaming changed buffering");
        Console.WriteLine("Updater support: Swiss path mapping, injection rejection and independent HTTP limits passed.");
    }
}
