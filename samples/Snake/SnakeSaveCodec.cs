using System;

/// <summary>Portable, checked little-endian binary save with version and FNV-1a checksum.</summary>
internal static class SnakeSaveCodec
{
    private const int Header=24;
    private const int Version=1;
    private static void Put16(byte[] data,int at,int value)
    {
        data[at]=(byte)value;data[at+1]=(byte)(value>>8);
    }
    private static int Get16(byte[] data,int at)=>data[at]|data[at+1]<<8;
    private static void Put32(byte[] data,int at,uint value)
    {
        data[at]=(byte)value;data[at+1]=(byte)(value>>8);
        data[at+2]=(byte)(value>>16);data[at+3]=(byte)(value>>24);
    }
    private static uint Get32(byte[] data,int at)=>
        (uint)data[at]|((uint)data[at+1]<<8)|((uint)data[at+2]<<16)|((uint)data[at+3]<<24);

    private static uint Checksum(byte[] data)
    {
        uint hash=2166136261u;
        for(int i=0;i<data.Length;i++)
        {
            if(i>=20 && i<24)continue;
            unchecked{hash^=data[i];hash*=16777619u;}
        }
        return hash;
    }

    internal static byte[] Encode(SnakeGame game)
    {
        if(!game.IsAlive || game.Food<0)
            throw new InvalidOperationException("Only active games can be saved.");
        var data=new byte[Header+game.Length*2];
        data[0]=(byte)'D';data[1]=(byte)'S';data[2]=(byte)'N';data[3]=(byte)'K';
        data[4]=Version;data[5]=SnakeGame.Columns;data[6]=SnakeGame.Rows;
        data[7]=(byte)game.Direction;
        Put16(data,8,game.Length);Put16(data,10,game.Score);
        Put16(data,12,game.Food);Put16(data,14,game.ElapsedMilliseconds);
        Put32(data,16,game.RandomState);
        for(int i=0;i<game.Length;i++)Put16(data,Header+i*2,game.Segment(i));
        Put32(data,20,Checksum(data));
        return data;
    }

    /// <summary>Malformed, stale, corrupt or impossible saved states are rejected.</summary>
    internal static bool TryDecode(byte[]? data,out SnakeGame? restored)
    {
        restored=null;
        if(data==null || data.Length<Header+SnakeGame.StartingLength*2 ||
           data.Length>Header+SnakeGame.Capacity*2 ||
           data[0]!='D' || data[1]!='S' || data[2]!='N' || data[3]!='K' ||
           data[4]!=Version || data[5]!=SnakeGame.Columns || data[6]!=SnakeGame.Rows)
            return false;
        int direction=data[7],length=Get16(data,8),score=Get16(data,10);
        int food=Get16(data,12),elapsed=Get16(data,14);
        uint random=Get32(data,16);
        if(direction>3 || length<SnakeGame.StartingLength ||
           length>=SnakeGame.Capacity || score!=length-SnakeGame.StartingLength ||
           food>=SnakeGame.Capacity || elapsed>250 || random==0 ||
           data.Length!=Header+length*2 || Get32(data,20)!=Checksum(data))
            return false;

        var cells=new int[length];
        for(int i=0;i<length;i++)
        {
            int value=Get16(data,Header+i*2);
            if(value>=SnakeGame.Capacity || value==food)return false;
            for(int j=0;j<i;j++)if(cells[j]==value)return false;
            cells[i]=value;
            if(i>0)
            {
                int a=cells[i-1],b=cells[i];
                int dx=a%SnakeGame.Columns-b%SnakeGame.Columns;
                int dy=a/SnakeGame.Columns-b/SnakeGame.Columns;
                if((dx<0?-dx:dx)+(dy<0?-dy:dy)!=1)return false;
            }
        }
        int head=cells[0],neck=cells[1];
        int difference=head-neck;
        if((direction==SnakeGame.Up && difference!=-SnakeGame.Columns) ||
           (direction==SnakeGame.Right && difference!=1) ||
           (direction==SnakeGame.Down && difference!=SnakeGame.Columns) ||
           (direction==SnakeGame.Left && difference!=-1))return false;

        restored=SnakeGame.Restore(cells,length,direction,food,score,random,elapsed);
        return true;
    }
}
