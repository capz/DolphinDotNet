using System;

// Independent of GX so allocation policy and viewport accounting can be tested on the host.
internal sealed class BenchmarkModel
{
    internal const int MaxSlots = 96;
    internal const float HalfHeight = 2.8631188f, HalfWidth = HalfHeight * 4f / 3f;
    internal readonly int[] Ids = new int[MaxSlots], Wanted = new int[160];
    private readonly int[] seen = new int[1536];
    private readonly float[] areas = new float[1536], distances = new float[1536];
    internal int Group, BudgetKiB, Capacity, Count, WantedCount, DesiredCount;
    private int stamp;
    internal int SlotBytes => Group * Group * 4096;
    internal int Bytes => Count * SlotBytes;
    internal float ViewArea => 4 * HalfWidth * HalfHeight;
    internal void Configure(int group, int budgetKiB)
    {
        if ((group != 1 && group != 2 && group != 4) ||
            (budgetKiB != 192 && budgetKiB != 256 && budgetKiB != 384))
            throw new ArgumentException("Invalid comparison configuration.");
        Group = group; BudgetKiB = budgetKiB; Capacity = budgetKiB * 1024 / SlotBytes;
        Count = WantedCount = DesiredCount = 0;
        for (int i = 0; i < MaxSlots; i++) Ids[i] = -1;
    }
    internal static int Floor(float x) { int i = (int)x; return x < i ? i - 1 : i; }
    private static float Min(float a, float b) => a < b ? a : b;
    private static float Max(float a, float b) => a > b ? a : b;
    internal void Build(float cx, float cy)
    {
        stamp++; WantedCount = 0;
        float left = cx - HalfWidth, right = cx + HalfWidth;
        float bottom = cy - HalfHeight, top = cy + HalfHeight;
        // One-cell prefetch border. It always ranks behind any visible contribution.
        for (int y = Floor(bottom) - 1; y <= Floor(top) + 1; y++)
            for (int x = Floor(left) - 1; x <= Floor(right) + 1; x++)
            {
                if (x < 0 || x >= 48 || y < 0 || y >= 32) continue;
                int id = (y / Group) * (48 / Group) + x / Group;
                if (seen[id] != stamp)
                {
                    seen[id] = stamp; areas[id] = 0;
                    float dx = (x / Group) * Group + Group * .5f - cx;
                    float dy = (y / Group) * Group + Group * .5f - cy;
                    distances[id] = dx * dx + dy * dy;
                    Wanted[WantedCount++] = id;
                }
                float w = Max(0, Min(x + 1, right) - Max(x, left));
                float h = Max(0, Min(y + 1, top) - Max(y, bottom));
                areas[id] += w * h;
            }
        // Most on-screen area first; center distance breaks ties deterministically.
        for (int i = 1; i < WantedCount; i++)
        {
            int id = Wanted[i], j = i;
            while (j > 0 && (areas[id] > areas[Wanted[j - 1]] ||
                (areas[id] == areas[Wanted[j - 1]] && distances[id] < distances[Wanted[j - 1]])))
            { Wanted[j] = Wanted[j - 1]; j--; }
            Wanted[j] = id;
        }
        DesiredCount = WantedCount < Capacity ? WantedCount : Capacity;
    }
    internal int Find(int id)
    {
        for (int i = 0; i < Capacity; i++) if (Ids[i] == id) return i;
        return -1;
    }
    internal bool Desired(int id)
    {
        for (int i = 0; i < DesiredCount; i++) if (Wanted[i] == id) return true;
        return false;
    }
    internal int Victim()
    {
        for (int i = 0; i < Capacity; i++) if (Ids[i] < 0) return i;
        int victim = -1;
        for (int i = 0; i < Capacity; i++) if (!Desired(Ids[i]) &&
            (victim < 0 || Area(Ids[i]) < Area(Ids[victim]))) victim = i;
        return victim;
    }
    internal float Area(int id) => id >= 0 && seen[id] == stamp ? areas[id] : 0;
    internal void Remove(int slot) { if (Ids[slot] >= 0) { Ids[slot] = -1; Count--; } }
    internal void Commit(int slot, int id)
    {
        if (slot < 0 || slot >= Capacity || Ids[slot] >= 0 || Find(id) >= 0)
            throw new InvalidOperationException("Invalid benchmark cache transition.");
        Ids[slot] = id; Count++;
    }
    internal int Coverage()
    {
        float total = 0;
        for (int i = 0; i < Capacity; i++) total += Area(Ids[i]);
        int result = (int)(total / ViewArea * 10000 + .5f);
        return result > 10000 ? 10000 : result;
    }
    internal int UsefulKiB()
    {
        float total = 0;
        for (int i = 0; i < Capacity; i++) total += Area(Ids[i]);
        return (int)(total * 4);
    }
}

internal sealed class BenchmarkHistogram
{
    internal readonly int[] Frames = new int[2001], Latency = new int[2001];
    internal void Clear()
    { for (int i = 0; i < Frames.Length; i++) { Frames[i] = 0; Latency[i] = 0; } }
}

internal sealed class BenchmarkResult
{
    private BenchmarkHistogram? active;
    private int finalFrame95, finalFrame99, finalLatency95;
    internal readonly BenchmarkTiming Timing = new BenchmarkTiming();
    internal int Samples, Promotions, Evictions, Cancelled, RequestedKiB, UploadedKiB;
    internal int PeakKiB, StaticCoverage, TimeTo95Ms = -1, MaxFrameUs, MaxUploadUs, MaxSubmitUs;
    internal int LongestBelow95Ms;
    internal long DurationUs, MotionUs, CoverageTime, MotionCoverageTime;
    internal long PreparationUs,ReadyUs,QueueUs,BeginCopyUs,GateUs;
    internal int PreparationMaxUs,Reuses,Allocations,MaxPublications,Started,BeginCopies,PendingPeak,ReadyPeak;
    private long below95Us;
    internal bool Complete;
    private long completedAt;
    // The capture oracle samples at 2 Hz. Hold from actual completion, not
    // case start: a slow final measured frame can overshoot the nominal end.
    internal bool CanAdvance(long now,int dwellUs=1500000) => Complete && now - completedAt >= dwellUs;
    internal void Reset()
    {
        BeginCopies=PendingPeak=ReadyPeak=0;Timing.Reset();PreparationUs=ReadyUs=QueueUs=BeginCopyUs=GateUs=0;
        PreparationMaxUs=Reuses=Allocations=MaxPublications=Started=0;
        active = null; finalFrame95 = finalFrame99 = finalLatency95 = 0;
        Samples = Promotions = Evictions = Cancelled = RequestedKiB = UploadedKiB = 0;
        PeakKiB = StaticCoverage = MaxFrameUs = MaxUploadUs = MaxSubmitUs = LongestBelow95Ms = 0;
        DurationUs = MotionUs = CoverageTime = MotionCoverageTime = below95Us = 0;
        TimeTo95Ms = -1; Complete = false; completedAt = 0;
    }
    internal void Begin(BenchmarkHistogram histogram)
    { Reset(); histogram.Clear(); active = histogram; }
    internal void Finish(long now)
    {
        finalFrame95 = FramePercentile(95); finalFrame99 = FramePercentile(99);
        finalLatency95 = LatencyP95Ms; active = null; completedAt = now; Complete = true;
    }
    internal void Record(int elapsedMs, int frameUs, int coverage)
    {
        if (frameUs <= 0) return;
        Samples++; DurationUs += frameUs; CoverageTime += (long)coverage * frameUs;
        if (elapsedMs < 2000)
        {
            StaticCoverage = coverage;
            if (coverage >= 9500 && TimeTo95Ms < 0) TimeTo95Ms = elapsedMs;
        }
        else { MotionUs += frameUs; MotionCoverageTime += (long)coverage * frameUs; }
        int bin = frameUs / 250; if (bin > 2000) bin = 2000; active!.Frames[bin]++;
        if (frameUs > MaxFrameUs) MaxFrameUs = frameUs;
        if (coverage < 9500) below95Us += frameUs; else below95Us = 0;
        if (below95Us / 1000 > LongestBelow95Ms) LongestBelow95Ms = (int)(below95Us / 1000);
    }
    internal void Promoted(int latencyUs, int uploadUs)
    {
        Promotions++;
        int bin = latencyUs / 1000; if (bin > 2000) bin = 2000; active!.Latency[bin]++;
        if (uploadUs > MaxUploadUs) MaxUploadUs = uploadUs;
    }
    private static int Percentile(int[] histogram, int count, int percent, int units)
    {
        if (count == 0) return 0;
        int target = (count * percent + 99) / 100, sum = 0;
        for (int i = 0; i < histogram.Length; i++)
        { sum += histogram[i]; if (sum >= target) return (i + 1) * units; }
        return histogram.Length * units;
    }
    internal int FramePercentile(int percent)
    {
        if (active == null) return percent <= 95 ? finalFrame95 : finalFrame99;
        int value = Percentile(active.Frames, Samples, percent, 250);
        return value < MaxFrameUs ? value : MaxFrameUs;
    }
    internal int LatencyP95Ms => active == null ? finalLatency95 : Percentile(active.Latency, Promotions, 95, 1);
    internal int MeanCoverage => DurationUs == 0 ? 0 : (int)(CoverageTime / DurationUs);
    internal int MotionCoverage => MotionUs == 0 ? 0 : (int)(MotionCoverageTime / MotionUs);
}
