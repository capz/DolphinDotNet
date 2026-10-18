using System.Threading.Tasks;

internal static class Program
{
    private static async Task<int> Main()
    {
        KartNetwork.DirectPeer="127.0.0.1";
        using var network=new KartNetwork(11,28778,28779);
        var watch=Dolphin.Timing.Stopwatch.StartNew();
        var game=new KartGame();int phase=0;bool issued=false;
        for(int i=0;i<1200;i++)
        {
            game.Update(.01f,0,true,false,false,false);
            game.RaceTime=(float)watch.ElapsedSeconds;
            network.Update(.001f,game); // Deliberately unrelated to wall time.
            if(phase==0 && network.Peer.Paired)
            {
                if(network.Peer.Lap!=1 || network.Peer.X<49 || network.Peer.X>51)return 10;
                if(game.Stun<=0 || network.Peer.HitAcknowledgements[0]!=9001)return 12;
                phase=1;
            }
            else if(phase==1 && !network.Peer.Paired)phase=2;
            else if(phase==2 && network.Peer.Paired)
                return watch.ElapsedSeconds>3 && game.Time<watch.ElapsedSeconds/2 && issued && game.Items.Objects[0].Life==0 && game.Stun==0?0:11;
            if(phase==1 && !issued && game.Stun==0)
            {
                game.Items.Held=KartItem.Hazard;issued=game.Items.Use(game);
            }
            await Task.Delay(30);
        }
        return 20+phase;
    }
}
