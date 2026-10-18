using System;

internal static class KartItemTests
{
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static void Advance(KartGame game,float seconds)
    { for(int i=0;i<(int)(seconds*60)+1;i++)game.Update(KartGame.Step,0,false,false,false,false); }
    private static void Box(KartGame game,int index)
    { game.X=KartItems.BoxX(index);game.Z=KartItems.BoxZ(index);game.Update(KartGame.Step,0,false,false,false,false); }
    internal static void Run()
    {
        // Production pickups use seeded Random; replaying a seed remains reproducible.
        var seededA=new KartGame(1741);var seededB=new KartGame(1741);int kinds=0;
        for(int i=0;i<48;i++)
        {
            seededA.Items.Held=seededB.Items.Held=KartItem.None;
            seededA.Items.Boxes[0]=seededB.Items.Boxes[0]=0;
            Box(seededA,0);Box(seededB,0);
            Check(seededA.Items.Held==seededB.Items.Held,"Equal seeds reproduce pickup choices.");
            int kind=(int)seededA.Items.Held;
            Check(kind>=1&&kind<=3,"Random pickups contain a supported item.");kinds|=1<<kind;
        }
        Check(kinds==14,"Seeded pickups exercise every item category.");
        var game=new KartGame();Box(game,0);
        Check(game.Items.Held==KartItem.Turbo && game.Items.Boxes[0]>5,"Item box grants turbo and starts cooldown.");
        Check(game.Items.Use(game) && game.Boost==2 && game.Speed>=30,"L consumes turbo and accelerates immediately.");
        game.Speed=0;Advance(game,1.1f);Box(game,1);
        Check(game.Items.Held==KartItem.Hazard,"Next pickup grants a slick.");
        Check(game.Items.Use(game) && game.Items.Objects[0].Kind==KartItem.Hazard,"Slick is dropped behind the kart.");
        var slick=game.Items.Objects[0];
        game.X=slick.X;game.Z=slick.Z;
        game.Update(KartGame.Step,0,false,false,false,false);
        Check(game.Stun==0,"Freshly dropped own hazard has a grace period.");
        Advance(game,1.1f);
        Check(slick.Life==0 && game.Stun>0,"An old own slick causes an offline spin-out and is consumed.");
        Check(!game.Hit(),"Invulnerability prevents stacked damage.");
        Advance(game,2);Box(game,2);
        Check(game.Items.Held==KartItem.Bolt,"Pickup rotation includes a pulse projectile.");
        Check(game.Items.Use(game),"Projectile can be fired.");
        var bolt=game.Items.Objects[0];float z=bolt.Z;
        game.Items.Update(game,.1f);
        Check(bolt.Z!=z || bolt.X!=game.X,"Pulse advances along its heading.");
        game.Items.Update(game,5);Check(bolt.Life==0,"Projectiles expire.");
        for(int i=0;i<KartItems.Capacity;i++){game.Items.Objects[i].Life=10;game.Items.Objects[i].Kind=KartItem.Hazard;}
        game.Items.Held=KartItem.Hazard;
        Check(!game.Items.Use(game) && game.Items.Held==KartItem.Hazard,"Full object pool retains the held item.");
        var countdown=new KartGame { Countdown=3 };countdown.Items.Held=KartItem.Turbo;
        Check(!countdown.Items.Use(countdown),"Items cannot fire during the start countdown.");

        var owner=new KartGame { Z=3 };owner.Items.Held=KartItem.Hazard;owner.Items.Use(owner);
        var victim=new KartGame { Speed=20 };
        var a=new KartPeer(101);var b=new KartPeer(202);var packet=new byte[KartPeer.PacketSize];
        a.Write(packet,owner);Check(b.Accept(packet,packet.Length),"Remote item snapshot accepted.");
        b.Write(packet,victim);a.Accept(packet,packet.Length);
        a.Write(packet,owner);b.Accept(packet,packet.Length);
        b.Update(.3f);Check(!victim.Items.HitRemote(victim,b),"Stale snapshots cannot cause hits during packet loss.");
        a.Write(packet,owner);b.Accept(packet,packet.Length);
        Check(victim.Items.HitRemote(victim,b) && victim.Stun>0 && victim.Speed==5,"Victim detects and applies the remote hit.");
        int hit=b.HitAcknowledgements[0];Check(hit==owner.Items.Objects[0].Id,"Hit acknowledges the owner's item ID.");
        // Drop the first ACK, repeat the item snapshot, then deliver a later ACK.
        b.Write(packet,victim);
        a.Write(packet,owner);b.Accept(packet,packet.Length);
        victim.Invulnerable=0;victim.Stun=0;
        Check(!victim.Items.HitRemote(victim,b),"Repeated item snapshot cannot hit twice, even after invulnerability.");
        b.Write(packet,victim);a.Accept(packet,packet.Length);owner.Items.Acknowledge(a);
        Check(owner.Items.Objects[0].Life==0,"Repeated hit acknowledgement consumes the owner's object.");
        owner.Items.Update(owner,1.1f);owner.Z=-3;owner.Items.Held=KartItem.Bolt;owner.Items.Use(owner);
        Check(owner.Items.Objects[0].Id!=hit,"Slot reuse has a new item ID.");
        a.Write(packet,owner);b.Accept(packet,packet.Length);
        Check(victim.Items.HitRemote(victim,b),"A reused slot can cause a new hit.");
        var airborne=new KartGame { Height=4 };
        Check(!KartItems.Touches(airborne,new KartObject { X=52,Life=1,Kind=KartItem.Hazard }),"Gliders clear ground hazards.");
        a.Write(packet,owner);packet[64]=127;
        Check(!b.Accept(packet,packet.Length),"Reject invalid item kinds before accepting a packet.");
        var old=new byte[KartPeer.PacketSize];a.Write(old,owner);
        var fresh=new byte[KartPeer.PacketSize];a.Write(fresh,owner);b.Accept(fresh,fresh.Length);
        Check(!b.Accept(old,old.Length),"Out-of-order item snapshots are rejected.");
        b.Update(3.1f);Check(b.Objects[0].Life==0,"Disconnect clears remote item visuals.");
    }
}
