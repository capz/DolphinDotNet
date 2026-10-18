using System;
using Dolphin.Graphics;

internal sealed class KartHud : IDisposable
{
    private readonly Draw2D draw;
    private readonly Color32 cyan=new Color32(112,224,240),white=new Color32(231,239,252),
        dim=new Color32(65,103,133),gold=new Color32(255,210,104);
    private static readonly int[] glyphs={
        // A-Z then 0-9, packed row-major as 3 x 5 pixels.
        11245, 27566, 14627, 27502, 31143, 31140, 14699, 23533, 29847, 4714, 23469, 18727, 24557, 24573, 11114, 27556, 11131, 27565, 14478, 29842, 23407, 23402, 23549, 23213, 23186, 29351,
        31599, 11415, 25575, 25294, 23497, 31118, 14831, 29330, 31727, 31694
    };
    internal KartHud(GraphicsDevice device)
    { draw=new Draw2D(device,new Draw2DOptions { Width=640,Height=480,VertexCapacity=6144 }); }
    private void Rect(float x,float y,float w,float h,Color32 color)=>draw.FillRectangle(new RectF(x,y,w,h),color);
    private void Glyph(int glyph,float x,float y,float size,Color32 color)
    {
        for(int row=0;row<5;row++)for(int col=0;col<3;col++)
            if((glyph&(1<<(14-row*3-col)))!=0)Rect(x+col*size,y+row*size,size,size,color);
    }
    private void Text(string text,float x,float y,float size,Color32 color)
    {
        for(int i=0;i<text.Length;i++)
        {
            char c=text[i];
            if(c>='A' && c<='Z')Glyph(glyphs[c-'A'],x,y,size,color);
            else if(c>='0' && c<='9')Glyph(glyphs[26+c-'0'],x,y,size,color);
            x+=size*4;
        }
    }
    private void Number(int value,int digits,float x,float y,float size,Color32 color)
    {
        for(int i=digits-1;i>=0;i--)
        { Glyph(glyphs[26+value%10],x+i*size*4,y,size,color);value/=10; }
    }
    internal void Present(KartGame game,KartNetwork network,bool connected)
    {
        draw.Begin();
        Text("HARBOR LOOP",24,56,4,white);
        Text(network.Status,24,86,2,network.Peer.Connected?cyan:gold);
        Text("LAP",500,56,3,cyan);Number(game.Lap>=3?3:game.Lap+1,1,566,56,3,white);
        Text("TIME",24,390,2,cyan);Number((int)game.Time,4,24,409,3,white);
        Text("SPEED",194,390,2,cyan);Number((int)(game.Speed*3.6f),3,194,409,3,white);
        Text(game.Gliding?"GLIDING":game.Drifting?"DRIFT":game.Boost>0?"BOOST":"MINI TURBO",365,390,2,gold);
        Rect(365,410,150,9,dim);Rect(365,410,150*game.DriftCharge/1.4f,9,gold);
        if(network.Peer.Connected)
        {
            Text("PEER LAP",24,112,2,cyan);Number(network.Peer.Lap>=3?3:network.Peer.Lap+1,1,110,112,2,white);
            Text("GHOST CONTACT",24,130,2,dim);
        }
        if(network.Peer.Paired)
        { Text("PLACE",520,168,2,cyan);Number(network.Position(game),1,558,187,4,white); }
        // Map is an overview of the one fixed circuit, with live peer position.
        for(int i=0;i<32;i++)
        { float a=i*.19634954f;Rect(571+(float)System.Math.Cos(a)*39,110+(float)System.Math.Sin(a)*39,3,3,dim); }
        Rect(571+game.X*.75f,110+game.Z*.75f,5,5,gold);
        if(network.Peer.Connected)Rect(571+network.Peer.X*.75f,110+network.Peer.Z*.75f,5,5,cyan);
        Text("A DRIVE  B BRAKE  R DRIFT  X GLIDE  Y RESTART",24,450,2,white);
        Text("Z STATS   START B RESCUE   START X EXIT",24,467,2,dim);
        if(!connected)Text("CONNECT CONTROLLER",112,210,5,gold);
        if(game.Countdown>0)
        { Text("LAN RACE START",190,176,4,gold);Number((int)(game.Countdown+.999f),1,304,210,8,white); }
        if(game.Finished)
        { Rect(120,175,400,105,new Color32(12,24,42));Text("THREE LAPS COMPLETE",144,195,4,gold);Text("Y TO RACE AGAIN",200,244,3,white); }
        draw.End();
    }
    public void Dispose()=>draw.Dispose();
}
