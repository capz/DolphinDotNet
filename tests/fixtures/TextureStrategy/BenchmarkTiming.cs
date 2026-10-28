// Fixed storage: no per-frame allocations. GPU/VI are subsets of END;
// PREP runs on the background executor and may overlap GAP. Never sum those twice.
internal sealed class BenchmarkTiming
{
    internal const int Policy=0, Upload=1, Submit=2, End=3, Gpu=4, Vi=5, Gap=6, Other=7, Prep=8, Stages=9;
    internal readonly long[] Sum=new long[Stages], TailSum=new long[Stages];
    internal readonly int[] Max=new int[Stages], Worst=new int[Stages];
    // Eight largest correlated intervals cover exact P99 through 800 samples.
    internal readonly int[] Slow=new int[8*(Stages+2)];
    internal int P99Index=>Samples-(Samples*99+99)/100;
    internal int Samples, TailSamples, WorstUs, WorstDraws;
    internal static int Overlap(long frameStart,long frameEnd,long workStart,long workEnd)
    {
        if(workEnd<=workStart)return 0;
        long start=frameStart>workStart?frameStart:workStart;
        long end=frameEnd<workEnd?frameEnd:workEnd;
        return end>start?(int)(end-start):0;
    }
    internal void Reset()
    {
        Samples=TailSamples=WorstUs=WorstDraws=0;
        for(int i=0;i<Slow.Length;i++)Slow[i]=0;
        for(int i=0;i<Stages;i++){Sum[i]=TailSum[i]=0;Max[i]=Worst[i]=0;}
    }
    internal void Record(int frameUs,int[] stages,int draws)
    {
        int accounted=stages[Policy]+stages[Upload]+stages[Submit]+stages[End]+stages[Gap];
        stages[Other]=frameUs>accounted?frameUs-accounted:0;
        bool worst=frameUs>WorstUs, tail=frameUs>33333;
        Samples++;if(tail)TailSamples++;
        if(worst){WorstUs=frameUs;WorstDraws=draws;}
        for(int i=0;i<Stages;i++)
        {
            int value=stages[i];Sum[i]+=value;if(tail)TailSum[i]+=value;
            if(value>Max[i])Max[i]=value;if(worst)Worst[i]=value;
        }
        int stride=Stages+2,slot=0;
        while(slot<8&&Slow[slot*stride]>=frameUs)slot++;
        if(slot<8){
            for(int i=7;i>slot;i--)for(int k=0;k<stride;k++)Slow[i*stride+k]=Slow[(i-1)*stride+k];
            Slow[slot*stride]=frameUs;Slow[slot*stride+1]=draws;
            for(int k=0;k<Stages;k++)Slow[slot*stride+k+2]=stages[k];
        }
    }
    internal int Mean(int stage)=>Samples==0?0:(int)(Sum[stage]/Samples);
    internal int TailMean(int stage)=>TailSamples==0?0:(int)(TailSum[stage]/TailSamples);
}
