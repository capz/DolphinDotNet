using System;
using Dolphin.Graphics;

/// <summary>Original pixel art and shared bitmap text drawn through Draw2D.</summary>
internal sealed class GreenMenRenderer : IDisposable
{
    private readonly GraphicsDevice device;
    private readonly Draw2D draw;
    private readonly Color32 green=new Color32(128,245,115),cyan=new Color32(105,230,226),
        white=new Color32(228,244,226),dim=new Color32(51,82,73),amber=new Color32(255,200,103);
    private int frame;
    // Six rows per sprite, two animation frames for each of three original designs.
    private static readonly int[] sprites={
        24,60,126,219,126,36, 24,60,126,219,126,66,
        66,36,126,219,255,66, 66,165,126,219,126,36,
        60,126,219,255,90,129, 60,126,219,255,165,66
    };
    private readonly SampleText text=new SampleText();
    internal GreenMenRenderer(GraphicsDevice device)
    {
        this.device=device;
        draw=new Draw2D(device,new Draw2DOptions { Width=640,Height=480,VertexCapacity=12288 });
    }
    private void Rect(float x,float y,float w,float h,Color32 color)=>draw.FillRectangle(new RectF(x,y,w,h),color);

    private void Text(string value,float x,float y,float size,Color32 color)
    { text.Draw(draw,value,x,y,size,color); }
    private void Number(int value,int digits,float x,float y,float size,Color32 color)
    { text.Number(draw,value,digits,x,y,size,color); }
    private void Alien(float x,float y,int species,int beat,float scale,Color32 tint)
    {
        int start=(species*2+(beat&1))*6;
        for(int row=0;row<6;row++)for(int col=0;col<8;col++)
            if((sprites[start+row]&(1<<(7-col)))!=0)Rect(x+col*scale,y+row*scale,scale,scale,tint);
    }
    private void Ship(float x,float y,float size,Color32 tint)
    {
        Rect(x-2*size,y-5*size,4*size,3*size,tint);
        Rect(x-6*size,y-2*size,12*size,3*size,tint);
        Rect(x-7*size,y+size,14*size,3*size,tint);
    }
    internal void Present(GreenMenGame game,bool connected)
    {
        frame++;
        device.BeginFrame(new Color32(5,12,17));draw.Begin();
        for(int i=0;i<48;i++)
        {
            int x=(i*173+41)%620+10,y=(i*97+13)%344+92;
            Rect(x,y,i%7==0?2:1,2,dim);
        }
        Text("SCORE",28,46,2,green);Number(game.Score,6,28,62,2,white);
        Text("BEST",236,46,2,green);Number(game.HighScore,6,236,62,2,white);
        Text("WAVE",500,46,2,green);Number(game.Wave,3,500,62,2,white);
        Rect(28,84,584,1,dim);Rect(28,444,584,2,green);
        if(game.State==GreenMenState.Title)
        {
            Text("GREEN MEN",176,142,8,green);
            Text("DEFEND YOUR LITTLE CORNER OF SPACE",116,200,3,white);
            for(int i=0;i<3;i++)Alien(205+i*94,245,i,frame/28,5,i==1?cyan:green);
            Text("A TO START",240,316,4,white);
            Text("STICK OR DPAD TO MOVE",160,360,4,dim);
            Text("HOLD A TO FIRE",208,392,4,cyan);
        }
        else
        {
            for(int i=0;i<GreenMenGame.AlienCount;i++)if(game.Aliens[i])
                Alien(game.AlienX(i),game.AlienY(i),(i/GreenMenGame.Columns)%3,game.Beat,3,
                    i<GreenMenGame.Columns?cyan:green);
            for(int i=0;i<game.Shields.Length;i++)if(game.Shields[i])
                Rect(GreenMenGame.ShieldX(i),GreenMenGame.ShieldCellY(i),6,6,green);
            if(game.ShotActive)Rect(game.ShotX-2,game.ShotY,4,12,cyan);
            for(int i=0;i<GreenMenGame.BombCount;i++)if(game.Bombs[i])
            {
                Rect(game.BombX[i]-2,game.BombY[i],4,10,amber);
                Rect(game.BombX[i]+((frame/5+i)%2==0?-4:2),game.BombY[i]+3,2,4,amber);
            }
            if(game.Lives>0 && (game.Invulnerable<=0 || frame%12<6))Ship(game.PlayerX,GreenMenGame.PlayerY,2,cyan);
            if(!connected || game.State==GreenMenState.Paused || game.State==GreenMenState.GameOver || game.State==GreenMenState.WaveClear)
            {
                Rect(122,208,396,100,new Color32(5,12,17));
                if(!connected) { Text("CONNECT CONTROLLER 1",168,228,4,amber);Text("GAME FROZEN",232,266,4,dim); }
                else if(game.State==GreenMenState.Paused) { Text("PAUSED",248,228,6,white);Text("START TO RESUME",208,272,4,dim); }
                else if(game.State==GreenMenState.GameOver) { Text("GAME OVER",212,224,6,amber);Text("A TO TRY AGAIN",208,272,4,white); }
                else { Text("WAVE CLEARED",200,238,5,green); }
            }
        }
        for(int i=0;i<game.Lives;i++)Ship(42+i*28,463,1,cyan);
        Text("START PAUSE   B TITLE   Z STATS   X EXIT",176,457,2,dim);
        if(!connected && game.State==GreenMenState.Title)Text("CONNECT CONTROLLER 1",160,110,4,amber);
        draw.End();device.EndFrame();
    }
    public void Dispose(){text.Dispose();draw.Dispose();}
}
