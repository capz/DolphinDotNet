using Dolphin.Input;
internal static class Program
{
    private static int Main()
    {
        var input=new SampleInput(0,ControllerButton.L);
        var game=new KartGame { Countdown=3 };
        var local=new KartPeer(11);var remote=new KartPeer(22);var packet=new byte[KartPeer.PacketSize];
        for(int i=0;i<6;i++)
        {local.Write(packet,game);remote.Accept(packet,packet.Length);remote.Write(packet,game);local.Accept(packet,packet.Length);}
        if(!local.Paired||!remote.Paired)return 10;
        for(int frame=0;frame<330;frame++)
        {
            Controller.Update();var raw=Controller.GetState();var pad=input.Read();
            if(!raw.IsConnected)return 1;
            if(!pad.IsConnected)return 2;
            if(frame>=2&&frame<240&&!raw.A.IsHeld)return 3;
            if(frame>=2&&frame<240&&!pad.A.IsHeld)return 4;
            game.Update(KartGame.Step,pad.Stick.X,pad.IsConnected&&pad.A.IsHeld,pad.B.IsHeld,pad.R.IsHeld,pad.X.IsHeld);
            if(frame==239)
            {
                if(game.Countdown>0)return 5;
                if(game.Speed<5)return 6;
                if(game.Z<2)return 7;
            }
            if(frame==269&&game.Speed!=0)return 8;
        }
        if(game.Speed<5||game.X>=KartGame.Radius||game.Heading>=0)return 9;
        return 0;
    }
}
