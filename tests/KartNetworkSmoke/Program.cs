using System.Threading.Tasks;

internal static class Program
{
    private static async Task<int> Main()
    {
        KartNetwork.DirectPeer="127.0.0.1";
        using var network=new KartNetwork(11,28778,28779);
        var game=new KartGame();int phase=0;
        for(int i=0;i<1200;i++)
        {
            game.Update(.01f,0,true,false,false,false);
            network.Update(.01f,game);
            if(phase==0 && network.Peer.Paired)
            {
                if(network.Peer.Lap!=1 || network.Peer.X<49 || network.Peer.X>51)return 10;
                phase=1;
            }
            else if(phase==1 && !network.Peer.Paired)phase=2;
            else if(phase==2 && network.Peer.Paired)return game.Time>3?0:11;
            await Task.Delay(10);
        }
        return 20+phase;
    }
}
