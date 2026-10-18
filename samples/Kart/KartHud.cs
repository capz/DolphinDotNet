using System;
using Dolphin.Graphics;

internal sealed class KartHud : IDisposable
{
    private readonly Draw2D draw;
    private string timeText="0.00",bestText="";
    private int shownTime=-1,shownBest=-1;
    private readonly Color32 cyan=new Color32(112,224,240),white=new Color32(231,239,252),
        dim=new Color32(65,103,133),gold=new Color32(255,210,104);
    private readonly SampleText text=new SampleText();
    internal KartHud(GraphicsDevice device)
    { draw=new Draw2D(device,new Draw2DOptions { Width=640,Height=480,VertexCapacity=6144 }); }
    private void Rect(float x,float y,float w,float h,Color32 color)=>draw.FillRectangle(new RectF(x,y,w,h),color);

    private void Text(string value,float x,float y,float size,Color32 color)
    { text.Draw(draw,value,x,y,size,color); }
    private void Number(int value,int digits,float x,float y,float size,Color32 color)
    { text.Number(draw,value,digits,x,y,size,color); }
    internal void Present(KartGame game,KartNetwork network,bool connected,int best=0)
    {
        draw.Begin();
        Text("HARBOR LOOP",24,56,4,white);
        Text(network.Status,24,86,2,network.Peer.Connected?cyan:gold);
        Text("LAP",500,56,3,cyan);Number(game.Lap>=3?3:game.Lap+1,1,566,56,3,white);
        int centiseconds=(int)(game.Elapsed*100);
        if(centiseconds!=shownTime){shownTime=centiseconds;timeText=$"{game.Elapsed:F2}";}
        Text("TIME",24,390,2,cyan);Text(timeText,24,409,3,white);
        Text("SPEED",194,390,2,cyan);Number((int)(game.Speed*3.6f),3,194,409,3,white);
        Text(game.Gliding?"GLIDING":game.Drifting?"DRIFT":game.Boost>0?"BOOST":"MINI TURBO",365,390,2,gold);
        Rect(365,410,150,9,dim);Rect(365,410,150*game.DriftCharge/1.4f,9,gold);
        if(network.Peer.Connected)
        {
            Text("PEER LAP",24,112,2,cyan);Number(network.Peer.Lap>=3?3:network.Peer.Lap+1,1,110,112,2,white);
            Text(network.Peer.Stun>0?"PEER SPIN":network.Peer.Boost>0?"PEER BOOST":network.Peer.Held==KartItem.None?"PEER EMPTY":"PEER HAS ITEM",24,130,2,dim);
        }
        if(network.Peer.Paired)
        { Text("PLACE",520,168,2,cyan);Number(network.Position(game),1,558,187,4,white); }
        if(best!=shownBest){shownBest=best;bestText=$"BEST {best/1000f:F2}";}
        if(best>0)Text(bestText,24,148,2,dim);
        // Map is an overview of the one fixed circuit, with live peer position.
        for(int i=0;i<32;i++)
        { float a=i*.19634954f;Rect(571+(float)System.Math.Cos(a)*39,110+(float)System.Math.Sin(a)*39,3,3,dim); }
        Rect(571+game.X*.75f,110+game.Z*.75f,5,5,gold);
        if(network.Peer.Connected)Rect(571+network.Peer.X*.75f,110+network.Peer.Z*.75f,5,5,cyan);
        string item=game.Items.Held==KartItem.Turbo?"TURBO":game.Items.Held==KartItem.Hazard?"SLICK":game.Items.Held==KartItem.Bolt?"PULSE":"EMPTY";
        Text("ITEM "+item+"   L USE",365,432,2,gold);
        if(game.Stun>0)Text("SPIN OUT",260,162,4,gold);
        Text("A DRIVE  B BRAKE  R DRIFT  X GLIDE  Y RESTART",24,450,2,white);
        Text("Z STATS   START B RESCUE   START X EXIT",24,467,2,dim);
        if(!connected)Text("CONNECT CONTROLLER",112,210,5,gold);
        if(game.Countdown>0)
        { Text("LAN RACE START",190,176,4,gold);Number((int)(game.Countdown+.999f),1,304,210,8,white); }
        if(game.Finished)
        { Rect(120,175,400,105,new Color32(12,24,42));Text("THREE LAPS COMPLETE",144,195,4,gold);Text("Y TO RACE AGAIN",200,244,3,white); }
        draw.End();
    }
    public void Dispose(){text.Dispose();draw.Dispose();}
}
