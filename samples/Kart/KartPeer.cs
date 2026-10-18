using System;

// Fixed, versioned, big-endian LAN datagrams. No object serialization or trust
// in remote physics. Victims own hit detection; repeated per-slot hit IDs
// acknowledge consumption without applying a duplicate slowdown.
internal sealed class KartPeer
{
    internal const int PacketSize=252,Room=701;
    internal readonly int Id;
    internal bool Connected;
    internal bool InsideLane=>Id<remoteId;
    internal bool Paired=>Connected && acknowledged==Id;
    internal float X,Z,Heading,Height,Speed,Age,RemoteTime;
    internal int Lap;
    internal KartItem Held;
    internal float Boost,Stun;
    internal readonly KartObject[] Objects=KartItems.CreateObjects();
    internal readonly int[] HitAcknowledgements=new int[KartItems.Capacity],ReceivedHits=new int[KartItems.Capacity];
    private int sequence,lastSequence,remoteId,acknowledged;
    private bool haveSequence;
    private float targetX,targetZ,targetHeading,targetHeight;
    internal KartPeer(int id){Id=id==0?1:id;}
    private static void Put(byte[] b,int at,int n)
    { b[at]=(byte)((uint)n>>24);b[at+1]=(byte)((uint)n>>16);b[at+2]=(byte)((uint)n>>8);b[at+3]=(byte)n; }
    private static int Get(byte[] b,int at)=>unchecked((int)((uint)b[at]<<24|(uint)b[at+1]<<16|(uint)b[at+2]<<8|b[at+3]));
    internal void Write(byte[] b,KartGame game)
    {
        Put(b,0,0x4b415232);Put(b,4,Room);Put(b,8,Id);Put(b,12,unchecked(++sequence));
        Put(b,16,(int)(game.X*1000));Put(b,20,(int)(game.Z*1000));
        Put(b,24,(int)(game.Heading*10000));Put(b,28,(int)(game.Height*1000));
        Put(b,32,(int)(game.Speed*1000));Put(b,36,game.Lap);Put(b,40,(int)(game.Elapsed*1000));Put(b,44,Connected?remoteId:0);
        Put(b,48,(int)game.Items.Held);Put(b,52,(int)(game.Boost*1000));Put(b,56,(int)(game.Stun*1000));
        for(int i=0;i<KartItems.Capacity;i++)
        {
            int at=60+i*32;var item=game.Items.Objects[i];bool active=item.Life>.001f;
            Put(b,at,active?item.Id:0);Put(b,at+4,active?(int)item.Kind:0);
            Put(b,at+8,active?(int)(item.X*1000):0);Put(b,at+12,active?(int)(item.Z*1000):0);
            Put(b,at+16,active?(int)(item.Heading*10000):0);Put(b,at+20,active?(int)(item.Height*1000):0);
            Put(b,at+24,active?(int)(item.Life*1000):0);Put(b,at+28,HitAcknowledgements[i]);
        }
    }
    internal bool Accept(byte[] b,int count)
    {
        if(b==null || b.Length<PacketSize || count!=PacketSize || Get(b,0)!=0x4b415232 || Get(b,4)!=Room)return false;
        int id=Get(b,8),next=Get(b,12);
        if(id==0 || id==Id || (Connected && id!=remoteId))return false;
        if(haveSequence && id==remoteId && unchecked(next-lastSequence)<=0)return false;
        int x=Get(b,16),z=Get(b,20),heading=Get(b,24),height=Get(b,28),speed=Get(b,32),lap=Get(b,36),time=Get(b,40);
        if(x< -80000 || x>80000 || z< -80000 || z>80000 || heading< -31416 || heading>31416 ||
           height<0 || height>30000 || speed<0 || speed>40000 || lap<0 || lap>3 || time<0 || time>86400000)return false;
        int held=Get(b,48),boost=Get(b,52),stun=Get(b,56);
        if(held<0 || held>3 || boost<0 || boost>2000 || stun<0 || stun>2000)return false;
        // Validate the entire datagram before advancing sequence or state.
        for(int i=0;i<KartItems.Capacity;i++)
        {
            int at=60+i*32,itemId=Get(b,at),kind=Get(b,at+4),ix=Get(b,at+8),iz=Get(b,at+12),
                ih=Get(b,at+16),heightValue=Get(b,at+20),life=Get(b,at+24),hit=Get(b,at+28);
            if(itemId<0 || hit<0 || ix< -80000 || ix>80000 || iz< -80000 || iz>80000 ||
               ih< -31416 || ih>31416 || heightValue<0 || heightValue>30000 || life<0 || life>15000 ||
               (itemId==0?(kind!=0 || life!=0):(kind<2 || kind>3 || life==0)))return false;
        }
        bool changed=id!=remoteId;
        for(int i=0;i<KartItems.Capacity;i++)
        {
            if(changed)HitAcknowledgements[i]=0;
            int at=60+i*32;var item=Objects[i];item.Id=Get(b,at);item.Kind=(KartItem)Get(b,at+4);
            item.X=Get(b,at+8)*.001f;item.Z=Get(b,at+12)*.001f;item.Heading=Get(b,at+16)*.0001f;
            item.Height=Get(b,at+20)*.001f;item.Life=Get(b,at+24)*.001f;ReceivedHits[i]=Get(b,at+28);
            if(item.Id==HitAcknowledgements[i])item.Life=0;
        }
        Held=(KartItem)held;Boost=boost*.001f;Stun=stun*.001f;
        targetX=x*.001f;targetZ=z*.001f;targetHeading=heading*.0001f;targetHeight=height*.001f;
        if(!Connected){X=targetX;Z=targetZ;Heading=targetHeading;Height=targetHeight;}
        Connected=true;acknowledged=Get(b,44);remoteId=id;lastSequence=next;haveSequence=true;Age=0;
        Speed=speed*.001f;Lap=lap;RemoteTime=time*.001f;
        return true;
    }
    internal void Update(float seconds)
    {
        float oldAge=Age;Age+=seconds;
        if(Age>3){Connected=false;haveSequence=false;for(int i=0;i<KartItems.Capacity;i++)Objects[i].Life=0;return;}
        if(!Connected)return;
        Boost=KartGame.Clamp(Boost-seconds,0,2);Stun=KartGame.Clamp(Stun-seconds,0,2);
        for(int i=0;i<KartItems.Capacity;i++)
        {
            var item=Objects[i];item.Life=KartGame.Clamp(item.Life-seconds,0,15);
            if(item.Kind!=KartItem.Bolt)continue;
            float advance=KartGame.Clamp(Age,0,.15f)-KartGame.Clamp(oldAge,0,.15f);
            item.X+=MathF.Sin(item.Heading)*KartItems.BoltSpeed*advance;
            item.Z+=MathF.Cos(item.Heading)*KartItems.BoltSpeed*advance;
        }
        float extrapolation=KartGame.Clamp(Age,0,.15f);
        float blend=KartGame.Clamp(seconds*12,0,1);
        X+=(targetX+MathF.Sin(targetHeading)*Speed*extrapolation-X)*blend;
        Z+=(targetZ+MathF.Cos(targetHeading)*Speed*extrapolation-Z)*blend;
        Heading=KartGame.Wrap(Heading+KartGame.Wrap(targetHeading-Heading)*blend);
        Height+=(targetHeight-Height)*blend;
    }
}
