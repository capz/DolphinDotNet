using System;

internal static class Program
{
    private static void Check(bool condition,string message)
    {
        if(!condition)throw new Exception(message);
    }

    private static byte[] Rechecksum(byte[] data)
    {
        uint hash=2166136261u;
        for(int i=0;i<data.Length;i++)
        {
            if(i>=20 && i<24)continue;
            unchecked{hash^=data[i];hash*=16777619u;}
        }
        data[20]=(byte)hash;data[21]=(byte)(hash>>8);
        data[22]=(byte)(hash>>16);data[23]=(byte)(hash>>24);
        return data;
    }

    private static SnakeGame Made(int[] positions,int direction,int food,int score=0)
    {
        return SnakeGame.Restore(positions,positions.Length,direction,food,score,0xBEEFC001u,0);
    }

    private static int Main()
    {
        var a=new SnakeGame(55u);
        var b=new SnakeGame(55u);
        Check(a.Food==b.Food && a.Food>=0 && a.Food<SnakeGame.Capacity,
              "Food placement must be deterministic and inside the board.");
        for(int i=0;i<a.Length;i++)Check(a.Segment(i)!=a.Food,"Fruit on body.");
        int initial=a.Segment(0);
        a.Turn(SnakeGame.Left);
        Check(a.Advance(0.10f)==SnakeMove.None && a.Segment(0)==initial,
              "Movement waits for a fixed grid step.");
        Check(a.Advance(0.10f)==SnakeMove.Moved &&
              a.Segment(0)==initial+1,"Reverse direction should be ignored.");
        a.Turn(SnakeGame.Up);
        a.Turn(SnakeGame.Left); // Cannot queue two turns before one movement.
        Check(a.Advance(0.2f)==SnakeMove.Moved &&
              a.Segment(0)==initial+1-SnakeGame.Columns,
              "Only first legal turn is accepted.");

        var grow=Made(new[]{57,56,55,54},SnakeGame.Right,58);
        Check(grow.Advance(0.165f)==SnakeMove.Ate &&
              grow.Score==1 && grow.Length==5 && grow.Segment(0)==58,
              "Eating extends the snake and increments the score.");
        byte[] original=SnakeSaveCodec.Encode(grow);
        Check(SnakeSaveCodec.TryDecode(original,out var loaded) && loaded!=null,
              "Round-trip state should restore.");
        Check(loaded!.Score==grow.Score && loaded.Length==grow.Length &&
              loaded.Food==grow.Food && loaded.Direction==grow.Direction &&
              loaded.RandomState==grow.RandomState,"Round-trip all game fields.");
        for(int i=0;i<grow.Length;i++)
            Check(loaded.Segment(i)==grow.Segment(i),"Round-trip body ordering.");
        Check(loaded.Advance(0.165f)==grow.Advance(0.165f) &&
              loaded.Segment(0)==grow.Segment(0),
              "Continuation must run the same deterministic simulation.");

        var damaged=(byte[])original.Clone();
        damaged[damaged.Length-1]^=0x20;
        Check(!SnakeSaveCodec.TryDecode(damaged,out _),"Reject checksum corruption.");
        damaged=(byte[])original.Clone();damaged[4]++;
        Check(!SnakeSaveCodec.TryDecode(damaged,out _),"Reject unsupported versions.");
        damaged=(byte[])original.Clone();damaged[24]=250;damaged[25]=1;
        Rechecksum(damaged);
        Check(!SnakeSaveCodec.TryDecode(damaged,out _),"Reject disconnected segments.");
        damaged=(byte[])original.Clone();damaged[12]=damaged[24];damaged[13]=damaged[25];
        Rechecksum(damaged);
        Check(!SnakeSaveCodec.TryDecode(damaged,out _),"Reject food on head.");
        Check(!SnakeSaveCodec.TryDecode(new byte[5],out _),"Reject truncation.");

        var freeTail=Made(new[]{28,27,1,2},SnakeGame.Right,18);
        freeTail.Turn(SnakeGame.Up);
        Check(freeTail.Advance(0.165f)==SnakeMove.Moved &&
              freeTail.Segment(0)==2,
              "Moving into vacated tail space is legal.");
        var wall=Made(new[]{25,24,23,22},SnakeGame.Right,18);
        Check(wall.Advance(0.165f)==SnakeMove.Died && !wall.IsAlive,
              "Board boundaries kill the snake.");
        var hit=Made(new[]{28,27,1,2,3},SnakeGame.Right,18,1);
        hit.Turn(SnakeGame.Up);
        Check(hit.Advance(0.165f)==SnakeMove.Died,
              "Collision with a non-tail segment ends the run.");
        try{SnakeSaveCodec.Encode(wall);throw new Exception("Dead run encoded");}
        catch(InvalidOperationException){}

        var timer=new SaveInterval();
        Check(!timer.Advance(119f) && timer.Advance(1f),"Autosave after 120 seconds.");
        timer.Failed();
        Check(!timer.Advance(14f) && timer.Advance(1f),"Failures retry after 15 seconds.");
        timer.Saved();
        Check(!timer.Advance(119f) && timer.Advance(1f),
              "Successful saves reset the two-minute interval.");
        CardHotplugTests.Run();
        Console.WriteLine("Snake gameplay, serialization, autosave and hotplug tests passed");
        return 0;
    }
}
