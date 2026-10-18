using System;

[Flags]
internal enum PongEvent
{
    None = 0,
    Paddle = 1,
    Wall = 2,
    Point = 4,
    MatchWon = 8
}

/// <summary>Deterministic, hardware-independent rules; coordinates are logical 640 x 480 pixels.</summary>
internal sealed class PongGame
{
    internal const float Width = 640f;
    internal const float Height = 480f;
    internal const float Wall = 24f;
    internal const float LeftX = 36f;
    internal const float RightX = 604f;
    internal const float PaddleWidth = 12f;
    internal const float PaddleHalfHeight = 43f;
    internal const float BallRadius = 7f;
    internal const int WinningScore = 7;

    private float vx, vy;
    private int serveNumber, nextDirection;

    internal float LeftY { get; private set; }
    internal float RightY { get; private set; }
    internal float BallX { get; private set; }
    internal float BallY { get; private set; }
    internal int LeftScore { get; private set; }
    internal int RightScore { get; private set; }
    /// <summary>0 = none, 1 = left, 2 = right.</summary>
    internal int Winner { get; private set; }
    internal bool IsPlaying { get; private set; }

    internal PongGame() { Reset(); }

    internal void Reset()
    {
        LeftScore=0;RightScore=0;Winner=0;IsPlaying=false;
        LeftY=Height/2;RightY=Height/2;
        serveNumber=0;nextDirection=1;
        BallX=Width/2;BallY=Height/2;vx=0;vy=0;
    }

    internal bool Serve()
    {
        if(IsPlaying || Winner!=0) return false;
        BallX=Width/2;BallY=Height/2;
        vx=315f*nextDirection;
        serveNumber++;
        vy=(serveNumber%3-1)*82f;
        IsPlaying=true;
        return true;
    }

    private static float Absolute(float v)=>MathF.Abs(v);
    private static float Clamp(float x,float low,float high)=>Math.Clamp(x,low,high);

    /// <summary>Input is -1 for up and +1 for down. Handles substeps to avoid tunnelling.</summary>
    internal PongEvent Update(float seconds,float leftInput,float rightInput)
    {
        if(!(seconds>=0f)) throw new ArgumentOutOfRangeException("seconds");
        if(seconds>0.05f)seconds=0.05f;
        leftInput=Clamp(leftInput,-1f,1f);
        rightInput=Clamp(rightInput,-1f,1f);
        PongEvent events=PongEvent.None;
        while(seconds>0f)
        {
            float dt=seconds>1f/120f?1f/120f:seconds;
            seconds-=dt;
            float minimum=Wall+PaddleHalfHeight+5f;
            float maximum=Height-minimum;
            LeftY=Clamp(LeftY+leftInput*385f*dt,minimum,maximum);
            RightY=Clamp(RightY+rightInput*385f*dt,minimum,maximum);
            if(!IsPlaying)continue;

            float previousX=BallX;
            BallX+=vx*dt;
            BallY+=vy*dt;

            float ceiling=Wall+BallRadius, floor=Height-Wall-BallRadius;
            if(BallY<ceiling && vy<0f)
            {
                BallY=ceiling+(ceiling-BallY);
                vy=-vy;
                events|=PongEvent.Wall;
            }
            else if(BallY>floor && vy>0f)
            {
                BallY=floor-(BallY-floor);
                vy=-vy;
                events|=PongEvent.Wall;
            }

            float leftFace=LeftX+PaddleWidth/2f+BallRadius;
            float rightFace=RightX-PaddleWidth/2f-BallRadius;
            if(vx<0f && previousX>=leftFace && BallX<=leftFace &&
               Absolute(BallY-LeftY)<=PaddleHalfHeight+BallRadius)
            {
                BallX=leftFace+(leftFace-BallX);
                vx=Clamp(-vx*1.07f,270f,720f);
                vy=Clamp(vy+((BallY-LeftY)/PaddleHalfHeight)*170f+
                    leftInput*34f,-490f,490f);
                events|=PongEvent.Paddle;
            }
            else if(vx>0f && previousX<=rightFace && BallX>=rightFace &&
                    Absolute(BallY-RightY)<=PaddleHalfHeight+BallRadius)
            {
                BallX=rightFace-(BallX-rightFace);
                vx=Clamp(-vx*1.07f,-720f,-270f);
                vy=Clamp(vy+((BallY-RightY)/PaddleHalfHeight)*170f+
                    rightInput*34f,-490f,490f);
                events|=PongEvent.Paddle;
            }

            if(BallX < -BallRadius || BallX > Width+BallRadius)
            {
                bool leftPoint=BallX > Width;
                if(leftPoint){LeftScore++;nextDirection=1;}
                else{RightScore++;nextDirection=-1;}
                IsPlaying=false;
                BallX=Width/2;BallY=Height/2;vx=0f;vy=0f;
                if(LeftScore>=WinningScore)Winner=1;
                if(RightScore>=WinningScore)Winner=2;
                return events|PongEvent.Point|(Winner!=0?PongEvent.MatchWon:PongEvent.None);
            }
        }
        return events;
    }
}
