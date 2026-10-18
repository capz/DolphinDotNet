using System;
using Dolphin.Graphics;

internal enum SnakeScreen { Title, Playing, Paused, GameOver }

/// <summary>Draw2D rectangles with shared bitmap text.</summary>
internal sealed class SnakeRenderer : IDisposable
{
    private readonly GraphicsDevice device;
    private readonly Draw2D batch;
    private readonly SampleText text=new SampleText();
    private readonly Color32 background=new Color32(12,20,29);
    private readonly Color32 field=new Color32(21,35,44);
    private readonly Color32 dark=new Color32(50,78,82);
    private readonly Color32 white=new Color32(224,236,224);
    private readonly Color32 green=new Color32(95,210,145);
    private readonly Color32 bright=new Color32(185,245,155);
    private readonly Color32 fruit=new Color32(255,113,108);
    private readonly Color32 disabled=new Color32(94,109,113);
    private const int OriginX=112,OriginY=91,Cell=16;

    internal SnakeRenderer(GraphicsDevice device)
    {
        this.device=device;
        batch=new Draw2D(device,new Draw2DOptions
        {
            Width=640,Height=480,VertexCapacity=16000
        });
    }
    private void Rect(float x,float y,float w,float h,Color32 color)=>
        batch.FillRectangle(new RectF(x,y,w,h),color);



    private void Text(string value,int x,int y,int size,Color32 color)
    { text.Draw(batch,value,x,y,size*1.5f,color); }

    internal void Present(SnakeGame game,SnakeScreen screen,
                          int selection,bool canContinue,string saveMessage,bool connected)
    {
        device.BeginFrame(background);
        batch.Begin();
        Rect(18,17,604,3,dark);
        Rect(18,460,604,3,dark);

        if(screen==SnakeScreen.Title)
        {
            Text("SNAKE",230,65,6,bright);
            Text("MEMORY CARD SAVE DEMO",176,145,2,white);
            MenuItem("CONTINUE",0,selection,canContinue,216);
            MenuItem("NEW GAME",1,selection,true,290);
            Text("UP DOWN - SELECT  A - PLAY",164,380,2,disabled);
            Text("X - EXIT",254,405,2,disabled);
        }
        else
        {
            Text("SNAKE",34,37,3,bright);
            Text("SCORE",410,38,2,white);
            Text(game.Score.ToString(),488,38,2,bright);
            Rect(OriginX-3,OriginY-3,SnakeGame.Columns*Cell+6,
                 SnakeGame.Rows*Cell+6,dark);
            Rect(OriginX,OriginY,SnakeGame.Columns*Cell,
                 SnakeGame.Rows*Cell,field);
            if(game.Food>=0)
                CellAt(game.Food,fruit,3);
            for(int i=game.Length-1;i>=0;i--)
                CellAt(game.Segment(i),i==0?bright:green,i==0?1:2);
            if(screen==SnakeScreen.Paused)
            {
                Rect(214,205,212,64,background);
                Text("PAUSED",246,219,4,white);
                Text("START RESUME",215,287,2,disabled);
            }
            else if(screen==SnakeScreen.GameOver)
            {
                Rect(188,192,264,106,background);
                Text("GAME OVER",208,204,4,fruit);
                Text("A NEW GAME",238,264,2,white);
                Text("B TITLE",254,286,2,white);
            }
            else if(!connected)
            {
                Rect(188,210,264,55,background);
                Text("CONNECT PAD 1",208,228,2,fruit);
            }
            if(screen==SnakeScreen.Playing)
                Text("START PAUSE  B SAVE AND TITLE",137,414,2,disabled);
            else if(screen==SnakeScreen.Paused)
                Text("B SAVE AND TITLE",218,414,2,disabled);
        }

        // File-system status stays visible regardless of the screen.
        Text(saveMessage,32,438,2,white);
        batch.End();
        device.EndFrame();
    }

    private void MenuItem(string label,int index,int selected,bool enabled,int y)
    {
        bool active=index==selected;
        Color32 tint=!enabled?disabled:active?bright:white;
        if(active && enabled)
        {
            Rect(145,y-10,350,56,dark);
            Text(">",170,y,4,bright);
        }
        Text(label,193,y,4,tint);
    }

    private void CellAt(int cell,Color32 color,int inset)
    {
        int x=OriginX+(cell%SnakeGame.Columns)*Cell+inset;
        int y=OriginY+(cell/SnakeGame.Columns)*Cell+inset;
        Rect(x,y,Cell-2*inset,Cell-2*inset,color);
    }

    public void Dispose(){text.Dispose();batch.Dispose();}
}
