using System;

// Fixed, versioned, big-endian LAN datagrams. No object serialization or trust
// in remote physics. A peer can affect only the non-colliding remote kart.
internal sealed class KartPeer
{
    internal const int PacketSize=48,Room=701;
    internal readonly int Id;
    internal bool Connected;
    internal bool InsideLane=>Id<remoteId;
    internal bool Paired=>Connected && acknowledged==Id;
    internal float X,Z,Heading,Height,Speed,Age,RemoteTime;
    internal int Lap;
    private int sequence,lastSequence,remoteId,acknowledged;
    private bool haveSequence;
    private float targetX,targetZ,targetHeading,targetHeight;
    internal KartPeer(int id){Id=id==0?1:id;}
    private static void Put(byte[] b,int at,int n)
    { b[at]=(byte)((uint)n>>24);b[at+1]=(byte)((uint)n>>16);b[at+2]=(byte)((uint)n>>8);b[at+3]=(byte)n; }
    private static int Get(byte[] b,int at)=>unchecked((int)((uint)b[at]<<24|(uint)b[at+1]<<16|(uint)b[at+2]<<8|b[at+3]));
    internal void Write(byte[] b,KartGame game)
    {
        Put(b,0,0x4b415231);Put(b,4,Room);Put(b,8,Id);Put(b,12,unchecked(++sequence));
        Put(b,16,(int)(game.X*1000));Put(b,20,(int)(game.Z*1000));
        Put(b,24,(int)(game.Heading*10000));Put(b,28,(int)(game.Height*1000));
        Put(b,32,(int)(game.Speed*1000));Put(b,36,game.Lap);Put(b,40,(int)(game.Time*1000));Put(b,44,Connected?remoteId:0);
    }
    internal bool Accept(byte[] b,int count)
    {
        if(b==null || b.Length<PacketSize || count!=PacketSize || Get(b,0)!=0x4b415231 || Get(b,4)!=Room)return false;
        int id=Get(b,8),next=Get(b,12);
        if(id==0 || id==Id || (Connected && id!=remoteId))return false;
        if(haveSequence && id==remoteId && unchecked(next-lastSequence)<=0)return false;
        int x=Get(b,16),z=Get(b,20),heading=Get(b,24),height=Get(b,28),speed=Get(b,32),lap=Get(b,36),time=Get(b,40);
        if(x< -80000 || x>80000 || z< -80000 || z>80000 || heading< -31416 || heading>31416 ||
           height<0 || height>30000 || speed<0 || speed>40000 || lap<0 || lap>3 || time<0 || time>86400000)return false;
        targetX=x*.001f;targetZ=z*.001f;targetHeading=heading*.0001f;targetHeight=height*.001f;
        if(!Connected){X=targetX;Z=targetZ;Heading=targetHeading;Height=targetHeight;}
        Connected=true;acknowledged=Get(b,44);remoteId=id;lastSequence=next;haveSequence=true;Age=0;
        Speed=speed*.001f;Lap=lap;RemoteTime=time*.001f;
        return true;
    }
    internal void Update(float seconds)
    {
        Age+=seconds;if(Age>3){Connected=false;haveSequence=false;return;}
        if(!Connected)return;
        float extrapolation=KartGame.Clamp(Age,0,.15f);
        float blend=KartGame.Clamp(seconds*12,0,1);
        X+=(targetX+MathF.Sin(targetHeading)*Speed*extrapolation-X)*blend;
        Z+=(targetZ+MathF.Cos(targetHeading)*Speed*extrapolation-Z)*blend;
        Heading=KartGame.Wrap(Heading+KartGame.Wrap(targetHeading-Heading)*blend);
        Height+=(targetHeight-Height)*blend;
    }
}
