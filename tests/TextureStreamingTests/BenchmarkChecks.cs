internal static class BenchmarkChecks
{
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static void Fill(BenchmarkModel m)
    {
        for (int i = 0; i < m.DesiredCount; i++) if (m.Find(m.Wanted[i]) < 0)
        {
            int victim = m.Victim(); Check(victim >= 0, "Desired visible resource must have a replaceable slot");
            m.Remove(victim); m.Commit(victim, m.Wanted[i]);
        }
    }
    internal static void Run()
    {
        foreach (int group in new[] { 1, 2, 4 }) foreach (int budget in new[] { 192, 256, 384 })
        {
            var model = new BenchmarkModel(); model.Configure(group, budget); model.Build(8, 12); Fill(model);
            Check(model.Bytes <= budget * 1024, "Comparison native-byte capacity");
            Check(model.Coverage() <= 10000 && model.Coverage() > 0, "Viewport coverage range");
            if (group == 1 || budget >= 256) Check(model.Coverage() == 10000, "Stationary visible detail must precede prefetch");
            if (group == 4 && budget == 192) Check(model.Coverage() >= 7499 && model.Coverage() <= 7501, "Three of four equal visible atlases cover 75 percent");
            // A camera jump must allow old border/visible entries to be replaced.
            model.Build(30.5f, 20); Fill(model);
            for (int i = 0; i < model.DesiredCount; i++) Check(model.Find(model.Wanted[i]) >= 0, "New visible priorities replace old resources");
            bool missingVisible = false, residentBorder = false;
            for (int i = 0; i < model.WantedCount; i++) if (model.Area(model.Wanted[i]) > 0 && model.Find(model.Wanted[i]) < 0) missingVisible = true;
            for (int i = 0; i < model.Capacity; i++) if (model.Ids[i] >= 0 && model.Area(model.Ids[i]) == 0) residentBorder = true;
            Check(!(missingVisible && residentBorder), "Off-screen prefetch must not pin memory needed on screen");
            if (budget == 384) Check(model.Coverage() == 10000, "Largest budget covers the automatic route footprint");
        }
        // Independently compare all three layouts against the existing source.
        var page = new byte[4096];
        foreach (int group in new[] { 1, 2, 4 })
        {
            var pixels = new byte[group * group * 4096];
            int id = (20 / group) * (48 / group) + 8 / group;
            BenchmarkSource.Fill(pixels, id, group);
            for (int dy = 0; dy < group; dy++) for (int dx = 0; dx < group; dx++)
            {
                TextureSource.Fill(page, (20 + dy) * 48 + 8 + dx, false, false);
                for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++) for (int c = 0; c < 4; c++)
                    Check(page[(y * 32 + x) * 4 + c] == pixels[((dy * 32 + y) * group * 32 + dx * 32 + x) * 4 + c], "Benchmark layouts must contain identical world texels");
            }
        }
        var result = new BenchmarkResult();
        var histogram = new BenchmarkHistogram(); result.Begin(histogram);
        result.Record(0, 10000, 10000); result.Record(3000, 30000, 5000);
        Check(result.MeanCoverage == 6250 && result.MotionCoverage == 5000, "Coverage must be weighted by elapsed time");
        Check(result.TimeTo95Ms == 0 && result.StaticCoverage == 10000 && result.LongestBelow95Ms == 30, "Cold and motion measurements remain distinct");
        Check(result.FramePercentile(99) >= 30000 && result.FramePercentile(99) <= 30250, "Frame percentile units");
        result.Promoted(1234, 500); Check(result.LatencyP95Ms == 2 && result.MaxUploadUs == 500, "Request latency and upload cost are distinct");
        result.Finish();
        var other = new BenchmarkResult(); other.Begin(histogram); other.Record(0, 50000, 1000);
        Check(result.FramePercentile(99) == 30000 && result.LatencyP95Ms == 2 && result.Complete, "Completed results survive histogram reuse");
        result.Reset(); Check(result.Samples == 0 && result.MeanCoverage == 0 && result.TimeTo95Ms == -1 && result.Promotions == 0, "Per-case reset");
        var timing = new BenchmarkTiming();var stages = new int[BenchmarkTiming.Stages];
        stages[BenchmarkTiming.Upload]=5000;stages[BenchmarkTiming.End]=12000;
        stages[BenchmarkTiming.Gpu]=2000;stages[BenchmarkTiming.Vi]=9000;
        stages[BenchmarkTiming.Gap]=1000;stages[BenchmarkTiming.Prep]=7000;
        timing.Record(20000,stages,12);
        Check(stages[BenchmarkTiming.Other]==2000,"Nested GPU/VI and background preparation must not be double counted");
        stages[BenchmarkTiming.Upload]=2000;stages[BenchmarkTiming.Gap]=30000;
        timing.Record(50000,stages,18);
        Check(timing.TailSamples==1 && timing.TailMean(BenchmarkTiming.Gap)==30000,"Tail timings include only over-budget frames");
        Check(timing.Max[BenchmarkTiming.Upload]==5000 && timing.Worst[BenchmarkTiming.Upload]==2000 && timing.WorstDraws==18,"Worst frame is a correlated snapshot, not independent maxima");
        timing.Reset();Check(timing.Samples==0 && timing.WorstUs==0 && timing.Sum[0]==0 && timing.Max[1]==0,"Timing reset");
        Console.WriteLine("Comparison policy, viewport, source and measurement tests: PASS");
    }
}
