using System;

internal enum KartItem { None,Turbo,Hazard,Bolt }

internal sealed class KartObject
{
    internal int Id;
    internal KartItem Kind;
    internal float X,Z,Heading,Height,Life,Age;
}

internal sealed class KartItems
{
    internal const int Capacity=6,BoxCount=6;
    internal const float BoltSpeed=32;
    private static int nextId;
    internal KartItem Held;
    internal readonly KartObject[] Objects=CreateObjects();
    internal readonly float[] Boxes=new float[BoxCount];
    private int pickups;
    private readonly Random? random;
    internal KartItems(int seed=0){if(seed!=0)random=new Random(seed);}
    private float useCooldown;

    internal static KartObject[] CreateObjects()
    {
        var objects=new KartObject[Capacity];
        for(int i=0;i<Capacity;i++)objects[i]=new KartObject();
        return objects;
    }
    internal static float BoxAngle(int index)=>.32f+index*MathF.PI/3;
    internal static float BoxX(int index)=>MathF.Cos(BoxAngle(index))*KartGame.Radius;
    internal static float BoxZ(int index)=>MathF.Sin(BoxAngle(index))*KartGame.Radius;
    internal static bool Touches(KartGame game,KartObject item)
    {
        float dx=game.X-item.X,dz=game.Z-item.Z;
        return dx*dx+dz*dz<3.24f && MathF.Abs(game.Height-item.Height)<1.4f;
    }
    internal void Update(KartGame game,float seconds)
    {
        useCooldown=KartGame.Clamp(useCooldown-seconds,0,1);
        for(int i=0;i<BoxCount;i++)
        {
            Boxes[i]=KartGame.Clamp(Boxes[i]-seconds,0,6);
            if(Held!=KartItem.None || Boxes[i]>0 || game.Countdown>0 || game.Finished || game.Height>1.5f)continue;
            float dx=game.X-BoxX(i),dz=game.Z-BoxZ(i);
            if(dx*dx+dz*dz<6.25f)
            {
                Held=(KartItem)(1+(random==null?pickups%3:random.Next(3)));pickups++;Boxes[i]=6;
            }
        }
        for(int i=0;i<Capacity;i++)
        {
            var item=Objects[i];if(item.Life<=0)continue;
            item.Life=KartGame.Clamp(item.Life-seconds,0,15);item.Age+=seconds;
            if(item.Kind==KartItem.Bolt)
            {
                item.X+=MathF.Sin(item.Heading)*BoltSpeed*seconds;
                item.Z+=MathF.Cos(item.Heading)*BoltSpeed*seconds;
                if(KartGame.Distance(item.X,item.Z)>78)item.Life=0;
            }
            // Own hazards are dangerous after a grace period; this also makes
            // all three items usable/testable when playing without a peer.
            if(item.Life>0 && item.Age>1 && Touches(game,item) && game.Hit())item.Life=0;
        }
    }
    internal bool Use(KartGame game)
    {
        if(Held==KartItem.None || useCooldown>0 || game.Countdown>0 || game.Finished || game.Stun>0)return false;
        if(Held==KartItem.Turbo)
        { game.Boost=2;game.Speed=MathF.Max(game.Speed,30); }
        else
        {
            int slot=-1;for(int i=0;i<Capacity;i++)if(Objects[i].Life<=0){slot=i;break;}
            if(slot<0)return false;
            var item=Objects[slot];
            nextId=nextId==int.MaxValue?1:nextId+1;
            item.Id=nextId;item.Kind=Held;item.Age=0;
            float ahead=Held==KartItem.Bolt?3:-3;
            item.X=game.X+MathF.Sin(game.Heading)*ahead;
            item.Z=game.Z+MathF.Cos(game.Heading)*ahead;
            item.Heading=game.Heading;item.Height=Held==KartItem.Bolt?game.Height:0;
            item.Life=Held==KartItem.Hazard?14:4;
        }
        Held=KartItem.None;useCooldown=1;return true;
    }
    internal bool HitRemote(KartGame game,KartPeer peer)
    {
        if(!peer.Paired || peer.Age>.25f)return false;
        for(int i=0;i<Capacity;i++)
        {
            var item=peer.Objects[i];
            if(item.Life<=0 || item.Id==peer.HitAcknowledgements[i] || !Touches(game,item))continue;
            if(!game.Hit())return false;
            peer.HitAcknowledgements[i]=item.Id;item.Life=0;return true;
        }
        return false;
    }
    internal void Acknowledge(KartPeer peer)
    {
        for(int i=0;i<Capacity;i++)
            if(Objects[i].Id!=0 && Objects[i].Id==peer.ReceivedHits[i])Objects[i].Life=0;
    }
}
