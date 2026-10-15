using System;

internal enum SnakeMove { None, Moved, Ate, Died, Won }

/// <summary>Deterministic gameplay with no device, graphics or filesystem dependencies.</summary>
internal sealed class SnakeGame
{
    internal const int Columns=26, Rows=18, Capacity=Columns*Rows;
    internal const int StartingLength=4;
    internal const int Up=0, Right=1, Down=2, Left=3;

    private readonly int[] body=new int[Capacity];
    private int length, direction, queuedDirection, food, score;
    private float elapsedMilliseconds;
    private uint randomState;
    private bool alive;

    internal int Length=>length;
    internal int Direction=>direction;
    internal int Food=>food;
    internal int Score=>score;
    internal bool IsAlive=>alive;
    internal uint RandomState=>randomState;
    internal int ElapsedMilliseconds=>(int)elapsedMilliseconds;
    internal int Segment(int index)=>body[index];
    internal int StepMilliseconds
    {
        get {int value=165-score*3;return value<67?67:value;}
    }

    internal SnakeGame(uint seed=0x5A17C3D1u)
    {
        NewGame(seed);
    }

    internal void NewGame(uint seed)
    {
        randomState=seed==0 ? 0x5A17C3D1u : seed;
        length=StartingLength;
        int cx=Columns/2,cy=Rows/2;
        for(int i=0;i<length;i++)body[i]=cy*Columns+cx-i;
        score=0;direction=Right;queuedDirection=Right;elapsedMilliseconds=0;
        alive=true;food=-1;SpawnFood();
    }

    private uint NextRandom()
    {
        uint x=randomState;
        unchecked {x^=x<<13;x^=x>>17;x^=x<<5;}
        randomState=x;
        return x;
    }

    private bool Contains(int cell)
    {
        for(int i=0;i<length;i++)if(body[i]==cell)return true;
        return false;
    }

    private void SpawnFood()
    {
        int remaining=Capacity-length;
        if(remaining==0){food=-1;return;}
        int target=(int)(NextRandom()%(uint)remaining);
        for(int cell=0;cell<Capacity;cell++)
        {
            if(Contains(cell))continue;
            if(target--==0){food=cell;return;}
        }
        throw new InvalidOperationException("No available fruit cell.");
    }

    internal void Turn(int desired)
    {
        if(!alive || desired<0 || desired>3)return;
        // Only one turn can be queued for a movement step: cannot reverse
        // through two inputs before the next grid movement.
        if(queuedDirection!=direction || (direction+2)%4==desired)return;
        queuedDirection=desired;
    }

    private SnakeMove Move()
    {
        direction=queuedDirection;
        int head=body[0],x=head%Columns,y=head/Columns;
        switch(direction)
        {
            case Up:y--;break;
            case Right:x++;break;
            case Down:y++;break;
            default:x--;break;
        }
        if(x<0 || x>=Columns || y<0 || y>=Rows)
        {alive=false;return SnakeMove.Died;}

        int next=y*Columns+x;
        bool eating=next==food;
        // The tail moves out of the way unless this step grows the snake.
        int occupied=eating ? length : length-1;
        for(int i=0;i<occupied;i++)
            if(body[i]==next){alive=false;return SnakeMove.Died;}

        int lengthAfter=eating?length+1:length;
        for(int i=lengthAfter-1;i>0;i--)body[i]=body[i-1];
        body[0]=next;
        length=lengthAfter;
        if(!eating)return SnakeMove.Moved;

        score++;
        if(length==Capacity){food=-1;alive=false;return SnakeMove.Won;}
        SpawnFood();
        return SnakeMove.Ate;
    }

    /// <summary>Advance simulation using clamped wall time. Pausing is owned by the caller.</summary>
    internal SnakeMove Advance(float seconds)
    {
        if(!alive)return SnakeMove.None;
        if(!(seconds>=0f))throw new ArgumentOutOfRangeException("seconds");
        if(seconds>0.25f)seconds=0.25f;
        elapsedMilliseconds+=seconds*1000f;
        SnakeMove result=SnakeMove.None;
        int count=0;
        while(alive && elapsedMilliseconds>=StepMilliseconds && count++<5)
        {
            elapsedMilliseconds-=StepMilliseconds;
            SnakeMove next=Move();
            if(next==SnakeMove.Died || next==SnakeMove.Won)return next;
            if(next==SnakeMove.Ate)result=SnakeMove.Ate;
            else if(result==SnakeMove.None)result=SnakeMove.Moved;
        }
        return result;
    }

    internal static SnakeGame Restore(int[] savedBody,int savedLength,int savedDirection,
        int savedFood,int savedScore,uint savedSeed,int savedElapsedMilliseconds)
    {
        var game=new SnakeGame();
        game.length=savedLength;
        for(int i=0;i<savedLength;i++)game.body[i]=savedBody[i];
        game.direction=savedDirection;game.queuedDirection=savedDirection;
        game.food=savedFood;game.score=savedScore;
        game.randomState=savedSeed;game.elapsedMilliseconds=savedElapsedMilliseconds;
        game.alive=true;
        return game;
    }
}
