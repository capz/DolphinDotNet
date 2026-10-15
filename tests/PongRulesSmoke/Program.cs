using System;

internal static class Program
{
    private static void Assert(bool value,string reason)
    {
        if(!value)throw new Exception(reason);
    }
    private static int Main()
    {
        var game=new PongGame();
        Assert(!game.IsPlaying && game.LeftScore==0 && game.RightScore==0,"Initial match state.");
        float initialX=game.BallX;
        game.Update(0.04f,0f,1f);
        Assert(game.BallX==initialX && game.RightY>240,"Paddles move before the serve; ball waits.");
        game.Reset();

        Assert(game.Serve() && !game.Serve(),"Only one active serve.");
        bool paddleHit=false;
        for(int i=0;i<240;i++)
        {
            var events=game.Update(1f/120f,0f,0f);
            if((events&PongEvent.Paddle)!=0){paddleHit=true;break;}
        }
        Assert(paddleHit && game.IsPlaying,"Ball should rebound from a centered paddle.");

        // Seven deliberately unreturned serves prove scoring, resets and winning.
        game.Reset();
        for(int point=1;point<=PongGame.WinningScore;point++)
        {
            Assert(game.Serve(),"Point can be served.");
            for(int step=0;step<400 && game.IsPlaying;step++)
                game.Update(1f/120f,0f,1f);
            Assert(!game.IsPlaying && game.LeftScore==point && game.RightScore==0,
                "Left player should score when right paddle is held down.");
        }
        Assert(game.Winner==1 && !game.Serve(),"Match must stop at seven.");
        game.Reset();
        Assert(game.LeftScore==0 && game.RightScore==0 && game.Winner==0 && game.Serve(),
               "Restart clears the match state.");

        byte[] paddle=PongWaveforms.Chirp(810,590,70,93);
        byte[] wall=PongWaveforms.Chirp(390,335,55,70);
        byte[] pointSound=PongWaveforms.Chirp(530,155,285,100);
        Assert(paddle.Length==1543 && wall.Length==1212 && pointSound.Length==6284,
               "Audio sample counts are deterministic.");
        bool audible=false;
        for(int i=0;i<paddle.Length;i++)if(paddle[i]!=128){audible=true;break;}
        Assert(audible && paddle[paddle.Length-1]>=126 && paddle[paddle.Length-1]<=130,
               "Chirp should decay to silence.");
        Assert(pointSound[0]!=pointSound[pointSound.Length-1],
               "Scoring chirp must contain sound.");
        Console.WriteLine("Pong rules and generated audio: OK");
        return 0;
    }
}
