using System;
using Dolphin.Graphics;

/// <summary>Only untextured rectangles: no textures, fonts or external assets.</summary>
internal sealed class PongRenderer : IDisposable
{
    private readonly GraphicsDevice device;
    private readonly Draw2D draw;
    private readonly Color32 light=new Color32(228,238,246);
    private readonly Color32 dim=new Color32(62,81,100);
    private readonly Color32 left=new Color32(106,223,210);
    private readonly Color32 right=new Color32(255,173,125);

    internal PongRenderer(GraphicsDevice device)
    {
        this.device=device;
        draw=new Draw2D(device,new Draw2DOptions
        {
            Width=640,Height=480,VertexCapacity=2048
        });
    }
    private void Rect(float x,float y,float w,float h,Color32 color)=>
        draw.FillRectangle(new RectF(x,y,w,h),color);

    internal void Present(PongGame game,bool leftConnected,bool rightConnected,bool paused)
    {
        device.BeginFrame(new Color32(12,18,31));
        draw.Begin();
        Rect(18,22,604,2,dim);
        Rect(18,456,604,2,dim);
        for(int y=42;y<440;y+=26)Rect(319,y,2,12,dim);

        Rect(PongGame.LeftX-PongGame.PaddleWidth/2f,
             game.LeftY-PongGame.PaddleHalfHeight,
             PongGame.PaddleWidth,2f*PongGame.PaddleHalfHeight,
             leftConnected?left:dim);
        Rect(PongGame.RightX-PongGame.PaddleWidth/2f,
             game.RightY-PongGame.PaddleHalfHeight,
             PongGame.PaddleWidth,2f*PongGame.PaddleHalfHeight,
             rightConnected?right:dim);

        Rect(game.BallX-PongGame.BallRadius,game.BallY-PongGame.BallRadius,
             PongGame.BallRadius*2f,PongGame.BallRadius*2f,light);

        Digit(game.LeftScore,257,67,game.Winner==1?left:light);
        Digit(game.RightScore,348,67,game.Winner==2?right:light);

        // Two controller status lights, no font or texture required.
        Rect(24,464,25,4,leftConnected?left:dim);
        Rect(591,464,25,4,rightConnected?right:dim);

        if(game.Winner!=0)
        {
            if(game.Winner==1)Rect(252,149,49,5,left);
            else Rect(343,149,49,5,right);
            Rect(263,192,114,4,dim);
        }
        else if(paused || !leftConnected || !rightConnected)
        {
            Rect(305,220,10,40,dim);
            Rect(325,220,10,40,dim);
        }
        else if(!game.IsPlaying)
        {
            Rect(303,261,34,4,dim);
            Rect(314,251,12,4,light);
        }

        draw.End();
        device.EndFrame();
    }

    private static int Segments(int n)
    {
        switch(n)
        {
            case 0:return 0x3f;
            case 1:return 0x06;
            case 2:return 0x5b;
            case 3:return 0x4f;
            case 4:return 0x66;
            case 5:return 0x6d;
            case 6:return 0x7d;
            case 7:return 0x07;
            case 8:return 0x7f;
            default:return 0x6f;
        }
    }
    private void Digit(int number,float x,float y,Color32 tint)
    {
        const float w=40f, h=70f, t=6f;
        int parts=Segments(number);
        if((parts&1)!=0)Rect(x+t,y,w-t*2,t,tint);
        if((parts&2)!=0)Rect(x+w-t,y+t,t,h/2-t,tint);
        if((parts&4)!=0)Rect(x+w-t,y+h/2,t,h/2-t,tint);
        if((parts&8)!=0)Rect(x+t,y+h-t,w-t*2,t,tint);
        if((parts&16)!=0)Rect(x,y+h/2,t,h/2-t,tint);
        if((parts&32)!=0)Rect(x,y+t,t,h/2-t,tint);
        if((parts&64)!=0)Rect(x+t,y+h/2-t/2,w-t*2,t,tint);
    }
    public void Dispose()=>draw.Dispose();
}
