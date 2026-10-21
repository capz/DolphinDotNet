using System;

// Deterministic *logical* residency trace: unlike the rendering half, this does
// not move assets to ARAM or fetch storage. Provides comparable miss/byte counts.
internal static class CacheTrace
{
    private sealed class Cache
    {
        private readonly int[] ids, ages;
        private int tick;
        internal int Misses;
        internal Cache(int slots)
        {
            ids=new int[slots];ages=new int[slots];
            for(int i=0;i<slots;i++)ids[i]=-1;
        }
        internal void Touch(int id)
        {
            tick++;
            int victim=0;
            for(int i=0;i<ids.Length;i++)
            {
                if(ids[i]==id){ages[i]=tick;return;}
                if(ids[i]==-1){victim=i;break;}
                if(ages[i]<ages[victim])victim=i;
            }
            ids[victim]=id;ages[victim]=tick;Misses++;
        }
    }
    internal static (int Pages,int Atlases) Run()
    {
        // Equal nominal 192 KiB budget: 48 RGBA8 32x32 pages versus
        // three 128x128 atlases (64 KiB each); same texels, no compression.
        var pages=new Cache(48);
        var atlases=new Cache(3);
        for(int frame=0;frame<240;frame++)
        {
            int left=(frame/8)%7;
            // Sweep a 6x6 visible window, then reverse to test temporal reuse.
            if((frame/48)%2!=0)left=6-left;
            int top=(frame/40)%7;
            for(int y=top;y<top+6;y++)for(int x=left;x<left+6;x++)
            {
                pages.Touch(y*12+x);
                atlases.Touch((y/4)*3+x/4);
            }
        }
        return (pages.Misses,atlases.Misses);
    }
}
