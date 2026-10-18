using System;
using System.Net;
using System.Net.Sockets;

internal static class Program
{
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    private static void Main()
    {
        var game=new KartGame();
        for(int i=0;i<60;i++)game.Update(KartGame.Step,0,true,false,false,false);
        Check(game.Speed>10 && game.Z>5,"Offline acceleration and movement.");
        var right=new KartGame { Speed=20 };
        right.Update(.1f,1,true,false,false,false);
        Check(right.X<52,"Right steering follows the screen-space right turn.");
        float previous=game.Speed;
        for(int i=0;i<30;i++)game.Update(KartGame.Step,0,false,true,false,false);
        Check(game.Speed<previous,"Brake reduces speed.");
        var drift=new KartGame { Speed=20 };
        for(int i=0;i<60;i++){drift.X=52;drift.Z=0;drift.Update(KartGame.Step,.5f,true,false,true,false);}
        drift.Update(KartGame.Step,0,true,false,false,false);
        Check(drift.Boost>0,"Charged drift release grants a turbo.");
        var gates=new KartGame();
        for(int lap=0;lap<3;lap++)for(int gate=1;gate<=4;gate++)
        {
            float a=(gate%4)*MathF.PI/2+.05f;
            gates.X=MathF.Cos(a)*52;gates.Z=MathF.Sin(a)*52;gates.Heading=-a;
            gates.Update(KartGame.Step,0,false,false,false,false);
        }
        Check(gates.Lap==3 && gates.Finished,"Three ordered laps finish a run.");
        var reverse=new KartGame { Z=-1,Heading=MathF.PI };
        for(int i=0;i<20;i++)reverse.Update(KartGame.Step,0,false,false,false,false);
        Check(reverse.Lap==0,"Crossing the start in reverse cannot award laps.");
        var offroad=new KartGame { X=75,Speed=26 };
        offroad.Update(KartGame.Step,0,false,false,false,false);
        Check(offroad.Speed==0 && MathF.Abs(KartGame.Distance(offroad.X,offroad.Z)-52)<.01f,"Out-of-bounds rescue.");
        var jump=new KartGame { X=MathF.Cos(4.71f)*52,Z=MathF.Sin(4.71f)*52,Speed=15,Heading=-4.71f+2*MathF.PI };
        jump.Update(KartGame.Step,0,true,false,false,false);
        Check(jump.VerticalSpeed>0,"Jump strip launches the kart.");
        jump.Height=3;jump.VerticalSpeed=0;jump.Update(KartGame.Step,0,false,false,false,true);
        Check(jump.Gliding && jump.Height>2.99f,"Gliding reduces descent.");

        var aPeer=new KartPeer(11);var bPeer=new KartPeer(22);var bytes=new byte[KartPeer.PacketSize];
        aPeer.Write(bytes,game);
        Check(!aPeer.Accept(bytes,bytes.Length),"Ignore self broadcast.");
        Check(bPeer.Accept(bytes,bytes.Length) && bPeer.Connected,"Discover another peer.");
        Check(!bPeer.Accept(bytes,bytes.Length),"Reject duplicate packet.");
        Check(!bPeer.Paired,"Discovery requires a mutual acknowledgement.");
        bPeer.Write(bytes,game);Check(aPeer.Accept(bytes,bytes.Length) && aPeer.Paired,"Acknowledgement pairs the first peer.");
        aPeer.Write(bytes,game);Check(bPeer.Accept(bytes,bytes.Length) && bPeer.Paired,"Both peers acknowledge the pair.");
        Check(aPeer.InsideLane && !bPeer.InsideLane,"Peers choose distinct starting lanes.");
        aPeer.Write(bytes,game);bytes[0]=0;
        Check(!bPeer.Accept(bytes,bytes.Length),"Reject wrong protocol.");
        aPeer.Write(bytes,game);bytes[16]=127;
        Check(!bPeer.Accept(bytes,bytes.Length),"Reject out-of-range peer position.");
        Check(!bPeer.Accept(new byte[49],49),"Reject oversized datagrams.");
        var stranger=new KartPeer(33);stranger.Write(bytes,game);
        Check(!bPeer.Accept(bytes,bytes.Length),"A third player cannot replace the paired peer.");
        bPeer.Update(3.1f);Check(!bPeer.Connected,"Packet loss removes stale peer.");
        stranger.Write(bytes,game);Check(bPeer.Accept(bytes,bytes.Length),"New peers may pair after disconnect.");
        var restarted=new KartPeer(33);restarted.Write(bytes,game);
        bPeer.Update(3.1f);Check(bPeer.Accept(bytes,bytes.Length),"Restarted peer sequence is accepted after timeout.");
        var countdown=new KartGame { Countdown=3 };
        countdown.Update(.1f,0,true,false,false,false);
        Check(countdown.Time==0 && countdown.Speed==0 && countdown.Countdown<3,"Race countdown holds the start grid.");
        Check(MathF.Abs(KartGame.Wrap(3.2f)+3.083185f)<.001f,"Heading interpolation takes the short arc.");

        KartItemTests.Run();
        // Exercise the exact wire protocol through two real UDP endpoints.
        using var first=new UdpClient(new IPEndPoint(IPAddress.Loopback,0));
        using var second=new UdpClient(new IPEndPoint(IPAddress.Loopback,0));
        first.Client.ReceiveTimeout=second.Client.ReceiveTimeout=1000;
        var wireFirst=new KartPeer(101);
        wireFirst.Write(bytes,game);first.Send(bytes,bytes.Length,(IPEndPoint)second.Client.LocalEndPoint!);
        IPEndPoint endpoint=new IPEndPoint(IPAddress.Any,0);var received=second.Receive(ref endpoint);
        var receiver=new KartPeer(102);Check(receiver.Accept(received,received.Length),"UDP state arrives at peer.");
        receiver.Write(bytes,new KartGame());second.Send(bytes,bytes.Length,endpoint);
        received=first.Receive(ref endpoint);Check(wireFirst.Accept(received,received.Length),"Bidirectional peer exchange.");
        Console.WriteLine("Kart offline driving, drift, laps, glide, packet validation, loss/rejoin and UDP exchange passed.");
    }
}
