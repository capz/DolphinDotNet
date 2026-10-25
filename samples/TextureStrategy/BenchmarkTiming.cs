// Fixed storage: no per-frame allocations. GPU/VI are subsets of END;
// PREP runs on the background executor and may overlap GAP. Never sum those twice.
internal sealed class BenchmarkTiming
{
    internal const int Policy=0, Upload=1, Submit=2, End=3, Gpu=4, Vi=5, Gap=6, Other=7, Prep=8, Stages=9;
    internal readonly long[] Sum=new long[Stages], TailSum=new long[Stages];
    internal readonly int[] Max=new int[Stages], Worst=new int[Stages];
    internal int Samples, TailSamples, WorstUs, WorstDraws;
    internal void Reset()
    {
        Samples=TailSamples=WorstUs=WorstDraws=0;
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
    }
    internal int Mean(int stage)=>Samples==0?0:(int)(Sum[stage]/Samples);
    internal int TailMean(int stage)=>TailSamples==0?0:(int)(TailSum[stage]/TailSamples);
}
