using System;
using Dolphin.Graphics;

internal sealed class FoxHud : IDisposable
{
    private readonly Draw2D draw;
    private readonly Color32 cyan=new Color32(112,224,240),white=new Color32(231,239,252),
        dim=new Color32(65,103,133),gold=new Color32(255,210,104);
    private readonly SampleText text=new SampleText();
    internal FoxHud(GraphicsDevice device)
    { draw=new Draw2D(device,new Draw2DOptions { Width=640,Height=480,VertexCapacity=6144 }); }
    private void Rect(float x,float y,float w,float h,Color32 color)=>draw.FillRectangle(new RectF(x,y,w,h),color);

    private void Text(string value,float x,float y,float size,Color32 color)
    { text.Draw(draw,value,x,y,size,color); }
    private void Number(int value,int digits,float x,float y,float size,Color32 color)
    { text.Number(draw,value,digits,x,y,size,color); }
    internal void Present(FoxGame game,bool connected)
    {
        draw.Begin();
        Text("FOX",28,46,4,cyan);Text("SCORE",240,46,2,cyan);Number(game.Score,6,240,62,2,white);
        Text("BEST",480,46,2,cyan);Number(game.HighScore,6,480,62,2,white);
        if(game.State==FoxState.Playing || game.State==FoxState.Paused)
        {
            float x=320+game.PlayerX*8,y=240-game.PlayerY*8;
            Rect(x-13,y-13,7,2,cyan);Rect(x-13,y-13,2,7,cyan);
            Rect(x+6,y-13,7,2,cyan);Rect(x+11,y-13,2,7,cyan);
            Rect(x-13,y+11,7,2,cyan);Rect(x-13,y+6,2,7,cyan);
            Rect(x+6,y+11,7,2,cyan);Rect(x+11,y+6,2,7,cyan);
            if(game.BossActive)
            {
                Text("CORE",268,92,2,gold);Rect(184,108,272,8,dim);Rect(184,108,272*game.BossHealth/20f,8,gold);
            }
            else { Text("ROUTE",28,92,2,dim);Rect(28,108,120,4,dim);Rect(28,108,120*game.Distance/FoxGame.BossDistance,4,cyan); }
        }
        Text("SHIELD",28,410,2,cyan);
        for(int i=0;i<FoxGame.MaximumShield;i++)Rect(28+i*21,428,17,8,i<game.Shield?cyan:dim);
        Text("BOOST",484,410,2,cyan);Rect(484,428,128,8,dim);Rect(484,428,128*game.Energy,8,cyan);
        Text(game.RollCooldown<=0?"ROLL READY":"ROLL CHARGING",250,428,2,game.RollCooldown<=0?cyan:dim);
        Text("A LASER   X BOOST   Y BRAKE   LR ROLL   Z STATS",144,458,2,dim);
        if(game.State==FoxState.Title)
        {
            Rect(124,132,392,150,new Color32(7,16,36));
            Text("FOX",260,148,10,white);Text("A SMALL RAIL SHOOTER",168,220,4,cyan);
            Text("A TO LAUNCH",232,260,4,white);
            Text("STICK TO FLY   START PAUSE",128,306,4,white);
            Text("GOLD RINGS RESTORE SHIELD",128,338,4,gold);
            Text("X ON TITLE TO EXIT",176,376,4,dim);
        }
        else if(!connected || game.State!=FoxState.Playing)
        {
            Rect(120,166,400,132,new Color32(7,16,36));
            if(!connected) { Text("CONNECT CONTROLLER 1",160,194,4,gold);Text("FLIGHT FROZEN",216,246,4,dim); }
            else if(game.State==FoxState.Paused) { Text("PAUSED",248,190,6,white);Text("START TO RESUME",208,250,4,cyan); }
            else if(game.State==FoxState.Victory) { Text("MISSION CLEAR",164,190,6,gold);Text("A TO FLY AGAIN",208,250,4,white); }
            else { Text("SHIP LOST",212,190,6,gold);Text("A TO RETRY",240,250,4,white); }
        }
        if(!connected && game.State==FoxState.Title)Text("CONNECT CONTROLLER 1",160,110,4,gold);
        draw.End();
    }
    public void Dispose(){text.Dispose();draw.Dispose();}
}
